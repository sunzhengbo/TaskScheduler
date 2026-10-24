using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using TaskScheduler.Core.Listeners;
using TaskScheduler.Core.Models;
using TriggerState = Quartz.TriggerState;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 统一封装 Quartz Scheduler 的启动引导流程（供桌面端 App 与 CLI Daemon 模式共同复用）
/// </summary>
public static class SchedulerBootstrapService
{
    public const string SchedulerMutexName = "TaskScheduler-App-{3F7E9C15-2A8D-4E1B-9C6F-8D3A5B2E7F1C}";

    /// <summary>
    /// 检测当前系统中是否已有后台调度器实例（桌面端 App 或 CLI Daemon）正在运行
    /// </summary>
    public static bool IsBackgroundSchedulerRunning()
    {
        try
        {
            using var mutex = new Mutex(false, SchedulerMutexName);
            try
            {
                if (!mutex.WaitOne(0))
                {
                    return true;
                }

                mutex.ReleaseMutex();
                return false;
            }
            catch (AbandonedMutexException)
            {
                try { mutex.ReleaseMutex(); } catch { }
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启动 Quartz Scheduler，完成重调度、暂停恢复、开机任务触发等初始化流程
    /// </summary>
    public static async Task StartSchedulerCoreAsync(
        IServiceProvider serviceProvider,
        ISchedulerFactory schedulerFactory,
        bool triggerStartupTasks = true,
        CancellationToken ct = default)
    {
        var schedulerLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchedulerInit");
        var scheduler = await schedulerFactory.GetScheduler(ct).ConfigureAwait(false);

        var originallyPausedKeys = await RecordPausedTriggersAsync(scheduler, schedulerLogger, ct).ConfigureAwait(false);
        await RescheduleSimpleTriggersAsync(serviceProvider, schedulerLogger, ct).ConfigureAwait(false);
        await RestorePausedTriggersAsync(scheduler, originallyPausedKeys, schedulerLogger, ct).ConfigureAwait(false);

        // 注册作业执行生命周期监听器（在调度器启动前挂载完毕，避免首批即刻任务触发时遗漏完成监听）
        RegisterJobExecutionListener(serviceProvider, scheduler);

        // 阶段2：启动调度器（此时所有触发器已重调度完毕，不会 misfire）
        await scheduler.Start(ct).ConfigureAwait(false);
        schedulerLogger.LogInformation("Quartz Scheduler started");

        // 阶段3：调度器启动后，按需触发开机启动任务
        if (triggerStartupTasks)
        {
            await TriggerStartupTasksAsync(serviceProvider, schedulerLogger, ct).ConfigureAwait(false);
        }
    }

    private static void RegisterJobExecutionListener(IServiceProvider serviceProvider, IScheduler scheduler)
    {
        try
        {
            var ipcSyncService = serviceProvider.GetService<IIpcSyncService>();
            if (ipcSyncService == null) return;

            var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<JobExecutionSyncListener>();

            var exists = scheduler.ListenerManager.GetJobListeners()
                .Any(l => string.Equals(l.Name, JobExecutionSyncListener.ListenerName, StringComparison.Ordinal));
            if (!exists)
            {
                scheduler.ListenerManager.AddJobListener(
                    new JobExecutionSyncListener(ipcSyncService, logger),
                    GroupMatcher<JobKey>.AnyGroup());
            }
        }
        catch (Exception ex)
        {
            serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("SchedulerInit")
                .LogWarning(ex, "注册作业生命周期监听器失败");
        }
    }

    private static async Task<HashSet<TriggerKey>> RecordPausedTriggersAsync(
        IScheduler scheduler, ILogger logger, CancellationToken ct)
    {
        var pausedKeys = new HashSet<TriggerKey>();
        try
        {
            var allJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct).ConfigureAwait(false);
            foreach (var jk in allJobKeys)
            {
                var triggers = await scheduler.GetTriggersOfJob(jk, ct).ConfigureAwait(false);
                foreach (var t in triggers)
                {
                    try
                    {
                        var state = await scheduler.GetTriggerState(t.Key, ct).ConfigureAwait(false);
                        if (state == TriggerState.Paused)
                            pausedKeys.Add(t.Key);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "获取触发器 {TriggerKey} 状态失败", t.Key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取作业键列表失败");
        }

        return pausedKeys;
    }

    private static async Task RescheduleSimpleTriggersAsync(
        IServiceProvider serviceProvider, ILogger logger, CancellationToken ct)
    {
        try
        {
            var settingsService = serviceProvider.GetService<ISettingsService>();
            if (settingsService != null)
            {
                var engineJson = await settingsService.GetValueAsync("engine_settings", ct).ConfigureAwait(false);
                var engine = EngineSettings.FromJson(engineJson);
                if (engine.RescheduleOnStartup)
                {
                    var taskService = serviceProvider.GetService<ITaskSchedulerService>();
                    if (taskService != null)
                    {
                        await taskService.RescheduleAllSimpleTriggersAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        logger.LogWarning("ITaskSchedulerService 未注册，跳过 SimpleTrigger 重调度");
                    }
                }
                else
                {
                    logger.LogInformation("启动时重新调度已关闭，跳过 SimpleTrigger 重调度");
                }
            }
            else
            {
                logger.LogWarning("ISettingsService 未注册，跳过 SimpleTrigger 重调度");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时重新调度 SimpleTrigger 失败");
        }
    }

    private static async Task RestorePausedTriggersAsync(
        IScheduler scheduler, HashSet<TriggerKey> originallyPausedKeys, ILogger logger, CancellationToken ct)
    {
        foreach (var triggerKey in originallyPausedKeys)
        {
            try
            {
                await scheduler.PauseTrigger(triggerKey, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "恢复暂停触发器 {TriggerKey} 失败", triggerKey);
            }
        }

        if (originallyPausedKeys.Count > 0)
            logger.LogInformation("已重新暂停 {Count} 个用户暂停的触发器", originallyPausedKeys.Count);
    }

    private static async Task TriggerStartupTasksAsync(
        IServiceProvider serviceProvider, ILogger logger, CancellationToken ct)
    {
        try
        {
            var taskService = serviceProvider.GetService<ITaskSchedulerService>();
            if (taskService != null)
            {
                await taskService.TriggerAllStartupTasksAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "触发开机启动任务失败");
        }
    }
}
