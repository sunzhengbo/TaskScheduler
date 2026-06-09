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
    Task UpdateSimpleTriggerAsync(string triggerName, string triggerGroup, int repeatCount, TimeSpan interval, CancellationToken ct = default);
}
