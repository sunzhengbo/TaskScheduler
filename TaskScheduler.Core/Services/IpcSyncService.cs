using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quartz;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 基于 NamedPipe 的跨进程（CLI 与 GUI）实时同步服务实现
/// </summary>
public sealed class IpcSyncService(
    ISchedulerFactory schedulerFactory,
    ILogger<IpcSyncService> logger) : IIpcSyncService
{
    public const string PipeName = "TaskScheduler_IpcSync_Pipe";

    private readonly SemaphoreSlim _schedulerWakeLock = new(1, 1);
    private CancellationTokenSource? _serverCts;
    private bool _isServerRunning;

    public event Action<SyncMessage>? MessageReceived;

    public void StartServer()
    {
        if (_isServerRunning)
            return;

        _isServerRunning = true;
        _serverCts = new CancellationTokenSource();
        _ = RunServerLoopAsync(_serverCts.Token);
    }

    public async Task NotifyAsync(
        SyncEventType type,
        string? jobName = null,
        string? jobGroup = null,
        string? key = null,
        CancellationToken ct = default)
    {
        var msg = new SyncMessage
        {
            Type = type,
            JobName = jobName,
            JobGroup = jobGroup,
            Key = key
        };

        if (_isServerRunning)
        {
            InvokeMessageReceived(msg);
            return;
        }

        await SendToServerAsync(msg, ct).ConfigureAwait(false);
    }

    public async Task<bool> RequestTriggerJobNowAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var msg = new SyncMessage
        {
            Type = SyncEventType.TriggerJobNow,
            JobName = jobName,
            JobGroup = jobGroup
        };

        if (_isServerRunning)
        {
            return await HandleMessageInternalAsync(msg, ct).ConfigureAwait(false);
        }

        return await SendToServerAsync(msg, ct).ConfigureAwait(false);
    }

    private async Task<bool> SendToServerAsync(SyncMessage message, CancellationToken ct)
    {
        if (!SchedulerBootstrapService.IsBackgroundSchedulerRunning())
            return false;

        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await client.WriteAsync(bytes, timeoutCts.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: true);
            var ack = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            return ack == "OK";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "向宿主进程发送 IPC 同步通知未连接或超时: {Type}", message.Type);
            return false;
        }
    }

    private async Task RunServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                // 连接建立后交由独立后台任务读取与处理，主循环立即创建下一个管道实例迎接并发请求
                var connectedServer = server;
                server = null;
                _ = ProcessClientConnectionAsync(connectedServer, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (server != null)
                    await server.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                if (server != null)
                    await server.DisposeAsync().ConfigureAwait(false);

                logger.LogDebug(ex, "IPC 命名管道接收异常");
                try
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessClientConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                var success = true;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var msg = JsonSerializer.Deserialize<SyncMessage>(line);
                    if (msg != null)
                    {
                        success = await HandleMessageInternalAsync(msg, ct).ConfigureAwait(false);
                    }
                }

                // 回传 ACK 响应结果
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), bufferSize: 256, leaveOpen: true);
                await writer.WriteLineAsync((success ? "OK" : "ERROR").AsMemory(), ct).ConfigureAwait(false);
                await writer.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "处理 IPC 客户端消息流失败");
            }
        }
    }

    private async Task<bool> HandleMessageInternalAsync(SyncMessage msg, CancellationToken ct)
    {
        var success = true;
        try
        {
            if (msg.Type == SyncEventType.TriggerJobNow &&
                !string.IsNullOrWhiteSpace(msg.JobName) &&
                !string.IsNullOrWhiteSpace(msg.JobGroup))
            {
                var scheduler = await schedulerFactory.GetScheduler(ct).ConfigureAwait(false);
                await scheduler.TriggerJob(new JobKey(msg.JobName, msg.JobGroup), ct).ConfigureAwait(false);
            }
            else if (msg.Type == SyncEventType.TasksChanged)
            {
                await _schedulerWakeLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var scheduler = await schedulerFactory.GetScheduler(ct).ConfigureAwait(false);
                    if (scheduler.IsStarted && !scheduler.InStandbyMode)
                    {
                        var executing = await scheduler.GetCurrentlyExecutingJobs(ct).ConfigureAwait(false);
                        if (executing.Count == 0)
                        {
                            await scheduler.Standby(ct).ConfigureAwait(false);
                            await scheduler.Start(ct).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        _schedulerWakeLock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // 忽略容器销毁期间的信号量释放异常
                    }
                }
            }
        }
        catch (Exception ex)
        {
            success = false;
            logger.LogWarning(ex, "处理 IPC 调度同步动作失败: {Type}", msg.Type);
        }

        InvokeMessageReceived(msg);
        return success;
    }

    private void InvokeMessageReceived(SyncMessage msg)
    {
        try
        {
            MessageReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "执行 IPC 消息订阅回调失败: {Type}", msg.Type);
        }
    }

    public void Dispose()
    {
        _isServerRunning = false;
        try
        {
            _serverCts?.Cancel();
            _serverCts?.Dispose();
            _schedulerWakeLock.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
    }
}
