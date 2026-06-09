using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public class TaskSchedulerService : ITaskSchedulerService
{
    private readonly IScheduler _scheduler;

    public TaskSchedulerService(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// 将标准 5 字段 Unix cron 表达式转换为 Quartz.NET 6 字段格式。
    /// 若已是 6-7 字段则原样返回。
    /// </summary>
    private static string NormalizeCronExpression(string cronExpression)
    {
        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 5)
        {
            // 标准 Unix cron: 分 时 日 月 周 → Quartz: 秒 分 时 日 月 周
            // Quartz 要求 day-of-month 和 day-of-week 不能同时为 *，其中一个需改为 ?
            var dom = parts[2]; // day-of-month
            var dow = parts[4]; // day-of-week

            if (dom == "*" && dow == "*")
            {
                dow = "?";
            }
            else if (dom != "*" && dom != "?" && dow == "*")
            {
                dow = "?";
            }
            else if (dow != "*" && dow != "?" && dom == "*")
            {
                dom = "?";
            }

            return $"0 {parts[0]} {parts[1]} {dom} {parts[3]} {dow}";
        }

        return cronExpression;
    }

    public async Task<string> CreateTaskAsync(TaskCreateRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Group);
        ArgumentNullException.ThrowIfNull(request.JobType);

        var jobKey = new JobKey(request.Name, request.Group);

        var jobDataMap = new JobDataMap();
        if (request.JobData != null)
        {
            foreach (var kvp in request.JobData)
            {
                jobDataMap.Add(kvp.Key, kvp.Value);
            }
        }

        var jobDetail = JobBuilder.Create(request.JobType)
            .WithIdentity(jobKey)
            .WithDescription(request.Description)
            .UsingJobData(jobDataMap)
            .StoreDurably()
            .Build();

        ITrigger trigger;
        var triggerKey = new TriggerKey($"{request.Name}_trigger", request.Group);

        if (request.TriggerType == TriggerType.Simple)
        {
            trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithSimpleSchedule(x => x
                    .WithRepeatCount(request.RepeatCount)
                    .WithInterval(request.RepeatInterval)
                    .WithMisfireHandlingInstructionFireNow())
                .StartNow()
                .Build();
        }
        else
        {
            var normalizedCron = NormalizeCronExpression(request.CronExpression);
            trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(normalizedCron, x => x
                    .InTimeZone(TimeZoneInfo.Local)
                    .WithMisfireHandlingInstructionFireAndProceed())
                .StartNow()
                .Build();
        }

        await _scheduler.ScheduleJob(jobDetail, trigger, ct);
        return $"{request.Group}.{request.Name}";
    }

    public async Task DeleteTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await _scheduler.DeleteJob(jobKey, ct);
    }

    public async Task DeleteTaskById(string taskId, CancellationToken ct = default)
    {
        var (group, name) = ParseTaskId(taskId);
        await DeleteTaskAsync(name, group, ct);
    }

    public async Task UpdateTaskAsync(string jobName, string jobGroup, TaskUpdateRequest request, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await _scheduler.GetJobDetail(jobKey, ct);
        if (jobDetail == null)
        {
            throw new InvalidOperationException($"Job '{jobGroup}.{jobName}' not found.");
        }

        var jobDataMap = new JobDataMap();
        foreach (var key in jobDetail.JobDataMap.Keys)
        {
            var val = jobDetail.JobDataMap[key];
            if (val != null)
                jobDataMap[key] = val;
        }
        if (request.JobData != null)
        {
            foreach (var kvp in request.JobData)
            {
                if (kvp.Value is string or int or long or double or float or bool or decimal)
                    jobDataMap[kvp.Key] = kvp.Value;
            }
        }

        var newJob = JobBuilder.Create(jobDetail.JobType)
            .WithIdentity(jobKey)
            .WithDescription(request.Description ?? jobDetail.Description)
            .UsingJobData(jobDataMap)
            .StoreDurably()
            .Build();

        await _scheduler.AddJob(newJob, true, true, ct);
    }

    public async Task<ScheduledTaskDetail?> GetTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        return await GetTaskDetailAsync(jobKey, ct);
    }

    public async Task<ScheduledTaskDetail?> GetTaskById(string taskId, CancellationToken ct = default)
    {
        var (group, name) = ParseTaskId(taskId);
        return await GetTaskAsync(name, group, ct);
    }

    public async Task<IReadOnlyList<ScheduledTaskDetail>> GetAllTasksAsync(CancellationToken ct = default)
    {
        var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct);
        var tasks = new List<ScheduledTaskDetail>();

        foreach (var jobKey in jobKeys)
        {
            var task = await GetTaskDetailAsync(jobKey, ct);
            if (task != null)
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    public async Task<IReadOnlyList<ScheduledTaskDetail>> GetTasksByGroupAsync(string groupName, CancellationToken ct = default)
    {
        var matcher = GroupMatcher<JobKey>.GroupEquals(groupName);
        var jobKeys = await _scheduler.GetJobKeys(matcher, ct);
        var tasks = new List<ScheduledTaskDetail>();

        foreach (var jobKey in jobKeys)
        {
            var task = await GetTaskDetailAsync(jobKey, ct);
            if (task != null)
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    public async Task<IReadOnlyList<string>> GetJobGroupsAsync(CancellationToken ct = default)
    {
        var groups = await _scheduler.GetJobGroupNames(ct);
        return groups.ToList();
    }

    public async Task PauseTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await _scheduler.PauseJob(jobKey, ct);
    }

    public async Task ResumeTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await _scheduler.ResumeJob(jobKey, ct);
    }

    public async Task PauseTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        await _scheduler.PauseTrigger(triggerKey, ct);
    }

    public async Task ResumeTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        await _scheduler.ResumeTrigger(triggerKey, ct);
    }

    public async Task TriggerJobAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await _scheduler.TriggerJob(jobKey, ct);
    }

    public async Task UpdateCronTriggerAsync(string triggerName, string triggerGroup, string cronExpression, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        var oldTrigger = await _scheduler.GetTrigger(triggerKey, ct);
        if (oldTrigger == null)
        {
            throw new InvalidOperationException($"Trigger '{triggerGroup}.{triggerName}' not found.");
        }

        var normalizedCron = NormalizeCronExpression(cronExpression);
        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithCronSchedule(normalizedCron, x => x
                .InTimeZone(TimeZoneInfo.Local)
                .WithMisfireHandlingInstructionFireAndProceed())
            .StartNow()
            .Build();

        await _scheduler.RescheduleJob(triggerKey, newTrigger, ct);
    }

    public async Task UpdateSimpleTriggerAsync(string triggerName, string triggerGroup, int repeatCount, TimeSpan interval, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        var oldTrigger = await _scheduler.GetTrigger(triggerKey, ct);
        if (oldTrigger == null)
        {
            throw new InvalidOperationException($"Trigger '{triggerGroup}.{triggerName}' not found.");
        }

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithSimpleSchedule(x => x
                .WithRepeatCount(repeatCount)
                .WithInterval(interval)
                .WithMisfireHandlingInstructionFireNow())
            .StartNow()
            .Build();

        await _scheduler.RescheduleJob(triggerKey, newTrigger, ct);
    }

    private async Task<ScheduledTaskDetail?> GetTaskDetailAsync(JobKey jobKey, CancellationToken ct)
    {
        var jobDetail = await _scheduler.GetJobDetail(jobKey, ct);
        if (jobDetail == null)
        {
            return null;
        }

        var triggers = await _scheduler.GetTriggersOfJob(jobKey, ct);
        var triggerInfos = new List<TriggerInfo>();

        // 过滤掉 TriggerJob 产生的临时触发器，只保留任务本身的命名触发器
        var expectedTriggerName = $"{jobKey.Name}_trigger";
        foreach (var trigger in triggers)
        {
            if (trigger.Key.Name != expectedTriggerName)
                continue;

            var triggerInfo = await MapToTriggerInfoAsync(trigger, ct);
            if (triggerInfo != null)
            {
                triggerInfos.Add(triggerInfo);
            }
        }

        var result = new ScheduledTaskDetail
        {
            Name = jobDetail.Key.Name,
            Group = jobDetail.Key.Group,
            Description = jobDetail.Description,
            JobClassName = jobDetail.JobType.FullName ?? string.Empty,
            IsDurable = jobDetail.Durable,
            RequestsRecovery = jobDetail.RequestsRecovery,
            Triggers = triggerInfos
        };

        // 从 JobDataMap 提取业务字段（兼容新的 Command 和旧的 Commands 键）
        if (jobDetail.JobDataMap.TryGetValue("Command", out var commandVal))
        {
            result.CommandJson = commandVal?.ToString() ?? "{}";
        }
        else if (jobDetail.JobDataMap.TryGetValue("Commands", out var commandsVal))
        {
            result.CommandJson = commandsVal?.ToString() ?? "{}";
        }
        if (jobDetail.JobDataMap.TryGetValue("Priority", out var priorityVal))
        {
            if (Enum.TryParse<TaskPriority>(priorityVal?.ToString(), out var priority))
                result.Priority = priority;
        }
        if (jobDetail.JobDataMap.TryGetValue("UseBootTime", out var bootTimeVal))
        {
            result.UseBootTime = jobDetail.JobDataMap.GetBooleanValue("UseBootTime");
        }
        if (jobDetail.JobDataMap.TryGetValue("CreatedAt", out var createdAtVal)
            && DateTime.TryParse(createdAtVal?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt))
        {
            result.CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc).ToLocalTime();
        }
        if (jobDetail.JobDataMap.TryGetValue("UpdatedAt", out var updatedAtVal)
            && DateTime.TryParse(updatedAtVal?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            result.UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc).ToLocalTime();
        }

        // 旧任务的 JobData 中没有 CreatedAt/UpdatedAt，从触发器 StartTimeUtc 回退获取
        if (result.CreatedAt == null && triggers.Count > 0)
        {
            var startTimeUtc = triggers.Min(t => t.StartTimeUtc);
            result.CreatedAt = startTimeUtc.ToLocalTime().DateTime;
        }
        result.UpdatedAt ??= result.CreatedAt;

        return result;
    }

    private async Task<TriggerInfo?> MapToTriggerInfoAsync(ITrigger trigger, CancellationToken ct)
    {
        var state = await _scheduler.GetTriggerState(trigger.Key, ct);

        var triggerInfo = new TriggerInfo
        {
            Name = trigger.Key.Name,
            Group = trigger.Key.Group,
            State = MapTriggerState(state),
            NextFireTime = trigger.GetNextFireTimeUtc()?.ToLocalTime().DateTime,
            PreviousFireTime = trigger.GetPreviousFireTimeUtc()?.ToLocalTime().DateTime
        };

        switch (trigger)
        {
            case SimpleTriggerImpl simpleTrigger:
                triggerInfo.Type = TriggerType.Simple;
                triggerInfo.RepeatCount = simpleTrigger.RepeatCount;
                triggerInfo.RepeatInterval = simpleTrigger.RepeatInterval;
                break;
            case CronTriggerImpl cronTrigger:
                triggerInfo.Type = TriggerType.Cron;
                triggerInfo.CronExpression = cronTrigger.CronExpressionString;
                triggerInfo.TimeZoneId = cronTrigger.TimeZone?.Id ?? "UTC";
                break;
        }

        return triggerInfo;
    }

    private static Models.TriggerState MapTriggerState(Quartz.TriggerState state)
    {
        return state switch
        {
            Quartz.TriggerState.Normal => Models.TriggerState.Normal,
            Quartz.TriggerState.Paused => Models.TriggerState.Paused,
            Quartz.TriggerState.Complete => Models.TriggerState.Complete,
            Quartz.TriggerState.Blocked => Models.TriggerState.Blocked,
            Quartz.TriggerState.Error => Models.TriggerState.Error,
            Quartz.TriggerState.None => Models.TriggerState.None,
            _ => Models.TriggerState.None
        };
    }

    private static (string Group, string Name) ParseTaskId(string taskId)
    {
        var index = taskId.IndexOf('.');
        if (index < 0)
        {
            return ("DEFAULT", taskId);
        }

        var group = taskId[..index];
        var name = taskId[(index + 1)..];
        return (string.IsNullOrWhiteSpace(group) ? "DEFAULT" : group, name);
    }
}
