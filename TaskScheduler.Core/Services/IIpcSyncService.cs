using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 跨进程（CLI 与 GUI）状态同步与即时调度通知服务接口
/// </summary>
public interface IIpcSyncService : IDisposable
{
    /// <summary>
    /// 当收到本进程或外部进程的同步消息时触发
    /// </summary>
    event Action<SyncMessage>? MessageReceived;

    /// <summary>
    /// 在宿主进程（GUI 或 Daemon）中启动命名管道监听服务
    /// </summary>
    void StartServer();

    /// <summary>
    /// 广播状态变更通知（同时通知本进程订阅者与正在运行的宿主进程）
    /// </summary>
    Task NotifyAsync(SyncEventType type, string? jobName = null, string? jobGroup = null, string? key = null, CancellationToken ct = default);

    /// <summary>
    /// 请求正在运行的宿主调度器立即触发任务；若宿主在线并成功接收则返回 true
    /// </summary>
    Task<bool> RequestTriggerJobNowAsync(string jobName, string jobGroup, CancellationToken ct = default);
}
