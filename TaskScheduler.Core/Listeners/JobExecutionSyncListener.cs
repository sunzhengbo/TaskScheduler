using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Listener;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Core.Listeners;

/// <summary>
/// Quartz 作业执行生命周期监听器。
/// 在作业执行完成后（无论成功还是异常），延迟等待调度器完成 Trigger 状态持久化与并发排他锁释放，
/// 随后异步广播 <see cref="SyncEventType.TasksChanged"/>，驱动所有打开的 UI 视图与外部进程自动刷新任务状态。
/// </summary>
public class JobExecutionSyncListener(
    IIpcSyncService ipcSyncService,
    ILogger<JobExecutionSyncListener> logger) : JobListenerSupport
{
    public const string ListenerName = "JobExecutionSyncListener";

    /// <summary>
    /// Quartz 完成作业执行管道收尾并完成 Trigger 状态恢复到存储层的等待缓冲时长（毫秒）
    /// </summary>
    private const int StatePersistenceDelayMs = 250;

    public override string Name => ListenerName;

    public override Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        var jobKey = context.JobDetail.Key;

        // Quartz 在 JobWasExecuted 执行之后才会完成 JobRunShell 的 JobExecutionCompleted，
        // 进而将 Trigger 状态从 Blocked 恢复为 Normal 并解除锁定。
        // 因此通过后台轻微延迟（约 250ms）确保底层状态落盘完毕后，再广播通知。
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StatePersistenceDelayMs, CancellationToken.None).ConfigureAwait(false);
                await ipcSyncService.NotifyAsync(
                    SyncEventType.TasksChanged,
                    jobKey.Name,
                    jobKey.Group,
                    ct: CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "通知任务执行完成状态变更失败: {JobKey}", jobKey);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
