using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public interface ITaskSchedulerService
{
    Task<string> CreateTaskAsync(TaskCreateRequest request, CancellationToken ct = default);
    Task DeleteTaskAsync(string jobName, string jobGroup, CancellationToken ct = default);
    Task DeleteTaskById(string taskId, CancellationToken ct = default);
    Task UpdateTaskAsync(string jobName, string jobGroup, TaskUpdateRequest request, CancellationToken ct = default);
    Task<ScheduledTaskDetail?> GetTaskAsync(string jobName, string jobGroup, CancellationToken ct = default);
    Task<ScheduledTaskDetail?> GetTaskById(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledTaskDetail>> GetAllTasksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledTaskDetail>> GetTasksByGroupAsync(string groupName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetJobGroupsAsync(CancellationToken ct = default);
    Task PauseTaskAsync(string jobName, string jobGroup, CancellationToken ct = default);
    Task ResumeTaskAsync(string jobName, string jobGroup, CancellationToken ct = default);
    Task PauseTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default);
    Task ResumeTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default);
    Task TriggerJobAsync(string jobName, string jobGroup, CancellationToken ct = default);
    Task UpdateCronTriggerAsync(string triggerName, string triggerGroup, string cronExpression, CancellationToken ct = default);
    Task UpdateSimpleTriggerAsync(string triggerName, string triggerGroup, int repeatCount, TimeSpan interval, CancellationToken ct = default, bool useBootTime = false);

    /// <summary>
    /// 重新计算所有 SimpleTrigger 的起始时间并重新调度。
    /// 开机模式：以当前系统开机时间为锚点 + 间隔；非开机模式：以当前应用启动时间为锚点 + 间隔。
    /// </summary>
    Task RescheduleAllSimpleTriggersAsync(CancellationToken ct = default);
}
