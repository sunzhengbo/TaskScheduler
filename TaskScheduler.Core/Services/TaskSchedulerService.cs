using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public class TaskSchedulerService(IScheduler scheduler, ILogger<TaskSchedulerService> logger) : ITaskSchedulerService
{
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

    private static DateTimeOffset CalculateStartTime(bool useBootTime, TimeSpan interval)
    {
        if (!useBootTime)
            return DateTimeOffset.UtcNow.Add(interval);

        // 使用系统启动时间戳计算开机时间点
        var bootTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(Environment.TickCount64 / 1000.0);
        return bootTime + interval;
    }

    private static string GetTriggerName(string jobName) => $"{jobName}_trigger";

    /// <summary>
    /// 计算 SimpleTrigger 重新调度后的剩余触发次数。
    /// Quartz 中 RepeatCount=N 表示总触发 N+1 次，TimesTriggered 为已触发次数。
    /// 剩余总次数 = RepeatCount + 1 - TimesTriggered，
    /// 但 WithRepeatCount(M) 创建的新触发器总触发 M+1 次，
    /// 因此需设为 (剩余总次数 - 1) = RepeatCount - TimesTriggered。
    /// </summary>
    private static int CalculateRemainingCount(int repeatCount, int timesTriggered)
    {
        if (repeatCount == -1) return -1;
        return Math.Max(0, repeatCount - timesTriggered);
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
        var triggerKey = new TriggerKey(GetTriggerName(request.Name), request.Group);

        if (request.TriggerType == TriggerType.Simple)
        {
            var startTime = CalculateStartTime(request.UseBootTime, request.RepeatInterval);
            trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithSimpleSchedule(x => x
                    .WithRepeatCount(request.RepeatCount)
                    .WithInterval(request.RepeatInterval)
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .StartAt(startTime)
                .Build();
        }
        else if (request.TriggerType == TriggerType.OnStartup)
        {
            jobDataMap["RunOnStartup"] = true.ToString();
            jobDetail = JobBuilder.Create(request.JobType)
                .WithIdentity(jobKey)
                .WithDescription(request.Description)
                .UsingJobData(jobDataMap)
                .StoreDurably()
                .Build();
            await scheduler.AddJob(jobDetail, replace: true, ct);
            return $"{request.Group}.{request.Name}";
        }
        else
        {
            var normalizedCron = NormalizeCronExpression(request.CronExpression);
            trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithCronSchedule(normalizedCron, x => x
                    .InTimeZone(TimeZoneInfo.Local)
                    .WithMisfireHandlingInstructionFireAndProceed())
                .StartAt(DateTimeOffset.UtcNow)
                .Build();
        }

        await scheduler.ScheduleJob(jobDetail, trigger, ct);
        return $"{request.Group}.{request.Name}";
    }

    public async Task DeleteTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await scheduler.DeleteJob(jobKey, ct);
    }

    public async Task DeleteTaskById(string taskId, CancellationToken ct = default)
    {
        var (group, name) = ParseTaskId(taskId);
        await DeleteTaskAsync(name, group, ct);
    }

    public async Task UpdateTaskAsync(string jobName, string jobGroup, TaskUpdateRequest request,
        CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
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

        await scheduler.AddJob(newJob, true, true, ct);
    }

    public async Task<ScheduledTaskDetail?> GetTaskAsync(string jobName, string jobGroup,
        CancellationToken ct = default)
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
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct);
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

    public async Task<IReadOnlyList<ScheduledTaskDetail>> GetTasksByGroupAsync(string groupName,
        CancellationToken ct = default)
    {
        var matcher = GroupMatcher<JobKey>.GroupEquals(groupName);
        var jobKeys = await scheduler.GetJobKeys(matcher, ct);
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
        var groups = await scheduler.GetJobGroupNames(ct);
        return groups.ToList();
    }

    public async Task PauseTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
        if (jobDetail == null) return;

        // OnStartup 任务没有真实触发器，PauseJob 是空操作
        // 改为在 JobDataMap 中标记暂停状态，供 GetTaskDetailAsync 和 TriggerAllStartupTasksAsync 读取
        if (IsRunOnStartup(jobDetail))
        {
            jobDetail.JobDataMap["IsPaused"] = "true";
            await scheduler.AddJob(jobDetail, replace: true, ct);
            return;
        }

        await scheduler.PauseJob(jobKey, ct);
    }

    public async Task ResumeTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
        if (jobDetail == null) return;

        if (IsRunOnStartup(jobDetail))
        {
            jobDetail.JobDataMap.Remove("IsPaused");
            await scheduler.AddJob(jobDetail, replace: true, ct);
            return;
        }

        await scheduler.ResumeJob(jobKey, ct);
    }

    public async Task PauseTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        await scheduler.PauseTrigger(triggerKey, ct);
    }

    public async Task ResumeTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        await scheduler.ResumeTrigger(triggerKey, ct);
    }

    public async Task TriggerJobAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await scheduler.TriggerJob(jobKey, ct);
    }

    public async Task UpdateCronTriggerAsync(string triggerName, string triggerGroup, string cronExpression,
        CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        var oldTrigger = await scheduler.GetTrigger(triggerKey, ct);
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
            .StartAt(DateTimeOffset.UtcNow)
            .Build();

        PreservePreviousFireTime(oldTrigger, newTrigger);

        await scheduler.RescheduleJob(triggerKey, newTrigger, ct);
    }

    public async Task UpdateSimpleTriggerAsync(string triggerName, string triggerGroup, int repeatCount,
        TimeSpan interval, CancellationToken ct = default, bool useBootTime = false)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        var oldTrigger = await scheduler.GetTrigger(triggerKey, ct);
        if (oldTrigger == null)
        {
            throw new InvalidOperationException($"Trigger '{triggerGroup}.{triggerName}' not found.");
        }

        var startTime = CalculateStartTime(useBootTime, interval);
        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .WithSimpleSchedule(x => x
                .WithRepeatCount(repeatCount)
                .WithInterval(interval)
                .WithMisfireHandlingInstructionNextWithRemainingCount())
            .StartAt(startTime)
            .Build();

        PreservePreviousFireTime(oldTrigger, newTrigger);

        await scheduler.RescheduleJob(triggerKey, newTrigger, ct);
    }

    public async Task UpdateOnStartupTriggerAsync(string triggerName, string triggerGroup,
        CancellationToken ct = default)
    {
        // 触发器名称格式为 {JobName}_trigger，还原为 JobKey
        var jobName = triggerName.EndsWith("_trigger", StringComparison.Ordinal)
            ? triggerName[..^"_trigger".Length]
            : triggerName;
        var jobKey = new JobKey(jobName, triggerGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
        if (jobDetail == null)
            throw new InvalidOperationException($"Job '{triggerGroup}.{jobName}' not found.");

        // 删除旧触发器（兼容旧数据：之前 OnStartup 有关联的 SimpleTrigger）
        var triggers = await scheduler.GetTriggersOfJob(jobKey, ct);
        foreach (var t in triggers)
        {
            await scheduler.UnscheduleJob(t.Key, ct);
        }

        jobDetail.JobDataMap["RunOnStartup"] = true.ToString();
        var updatedJob = JobBuilder.Create(jobDetail.JobType)
            .WithIdentity(jobKey)
            .WithDescription(jobDetail.Description)
            .UsingJobData(jobDetail.JobDataMap)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(updatedJob, replace: true, ct);
    }

    /// <inheritdoc />
    public async Task RescheduleAllSimpleTriggersAsync(CancellationToken ct = default)
    {
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct);
        var count = 0;

        foreach (var jobKey in jobKeys)
        {
            var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
            if (jobDetail == null)
                continue;

            var triggers = await scheduler.GetTriggersOfJob(jobKey, ct);
            foreach (var trigger in triggers)
            {
                if (trigger is not ISimpleTrigger simpleTrigger)
                    continue;

                // 只处理任务自身的命名触发器
                var expectedName = GetTriggerName(jobKey.Name);
                if (trigger.Key.Name != expectedName)
                    continue;

                var useBootTime = false;
                if (jobDetail.JobDataMap.TryGetValue("UseBootTime", out var bootVal))
                {
                    useBootTime = bool.TryParse(bootVal?.ToString(), out var b) && b;
                }

                // 跳过 OnStartup 任务，它们有自己的触发逻辑
                if (IsRunOnStartup(jobDetail))
                    continue;

                var interval = simpleTrigger.RepeatInterval;
                if (interval <= TimeSpan.Zero) continue;

                // 记录原始触发器状态
                var originalState = await scheduler.GetTriggerState(trigger.Key, ct);

                // 已完成的触发器无需重新调度，跳过以避免意外多执行一次
                if (originalState == Quartz.TriggerState.Complete) continue;

                var startTime = CalculateStartTime(useBootTime, interval);

                // 确保 startTime 在未来，避免 UseBootTime 模式下计算出过去时间
                // 导致 Quartz 重调度后再次触发 misfire
                var now = DateTimeOffset.UtcNow;
                if (startTime <= now)
                {
                    var intervalsBehind =
                        (long)Math.Ceiling((now - startTime).TotalMilliseconds / interval.TotalMilliseconds);
                    startTime = startTime.AddTicks(interval.Ticks * intervalsBehind);
                    if (startTime <= now)
                        startTime = startTime.Add(interval);
                }

                var remainingCount = CalculateRemainingCount(simpleTrigger.RepeatCount, simpleTrigger.TimesTriggered);

                var newTrigger = TriggerBuilder.Create()
                    .WithIdentity(trigger.Key)
                    .WithSimpleSchedule(x => x
                        .WithRepeatCount(remainingCount)
                        .WithInterval(interval)
                        .WithMisfireHandlingInstructionNextWithRemainingCount())
                    .StartAt(startTime)
                    .Build();

                PreservePreviousFireTime(trigger, newTrigger);

                await scheduler.RescheduleJob(trigger.Key, newTrigger, ct);

                // 注：触发器的暂停/恢复由调用方（App.axaml.cs）统一通过
                // PauseAll() / ResumeAll() 管理，此处无需单独处理

                count++;
                logger.LogInformation(
                    "重新调度 SimpleTrigger {TriggerKey}，UseBootTime={UseBootTime}，Interval={Interval}，StartTime={StartTime}",
                    trigger.Key, useBootTime, interval, startTime);
            }
        }

        logger.LogInformation("应用启动时重新调度了 {Count} 个 SimpleTrigger", count);
    }

    private static void PreservePreviousFireTime(ITrigger oldTrigger, ITrigger newTrigger)
    {
        var prevFireTime = oldTrigger.GetPreviousFireTimeUtc();
        if (!prevFireTime.HasValue) return;

        if (newTrigger is AbstractTrigger abstractTrigger)
            abstractTrigger.SetPreviousFireTimeUtc(prevFireTime);
    }

    private static bool IsRunOnStartup(IJobDetail jobDetail)
    {
        return jobDetail.JobDataMap.TryGetValue("RunOnStartup", out var val)
               && bool.TryParse(val?.ToString(), out var result) && result;
    }

    /// <inheritdoc />
    public async Task TriggerAllStartupTasksAsync(CancellationToken ct = default)
    {
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct);
        var count = 0;

        foreach (var jobKey in jobKeys)
        {
            var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
            if (jobDetail == null) continue;

            if (!IsRunOnStartup(jobDetail))
                continue;

            // 跳过已暂停的开机启动任务
            if (jobDetail.JobDataMap.TryGetValue("IsPaused", out var pausedVal)
                && bool.TryParse(pausedVal?.ToString(), out var paused) && paused)
            {
                logger.LogInformation("跳过已暂停的开机启动任务 {JobKey}", jobKey);
                continue;
            }

            await scheduler.TriggerJob(jobKey, ct);
            count++;
            logger.LogInformation("触发开机启动任务 {JobKey}", jobKey);
        }

        logger.LogInformation("应用启动时触发了 {Count} 个开机启动任务", count);
    }

    private async Task<ScheduledTaskDetail?> GetTaskDetailAsync(JobKey jobKey, CancellationToken ct)
    {
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct);
        if (jobDetail == null)
        {
            return null;
        }

        var triggers = await scheduler.GetTriggersOfJob(jobKey, ct);
        var triggerInfos = new List<TriggerInfo>();

        // 从 JobDataMap 提取 RunOnStartup 标志
        var runOnStartup = IsRunOnStartup(jobDetail);

        var expectedTriggerName = GetTriggerName(jobKey.Name);
        foreach (var trigger in triggers)
        {
            if (trigger.Key.Name != expectedTriggerName)
                continue;

            var triggerInfo = await MapToTriggerInfoAsync(trigger, runOnStartup, ct);
            if (triggerInfo != null)
            {
                triggerInfos.Add(triggerInfo);
            }
        }

        // OnStartup 任务可能没有触发器，或触发器状态为 Complete（已触发），
        // 为 UI 展示创建一个合成 TriggerInfo，确保仪表盘正确统计
        if (runOnStartup && (triggerInfos.Count == 0 || triggerInfos.All(t => t.State != Models.TriggerState.Normal)))
        {
            var isPaused = jobDetail.JobDataMap.TryGetValue("IsPaused", out var pausedVal)
                           && bool.TryParse(pausedVal?.ToString(), out var paused) && paused;

            triggerInfos.Add(new TriggerInfo
            {
                Name = expectedTriggerName,
                Group = jobKey.Group,
                Type = Models.TriggerType.OnStartup,
                State = isPaused ? Models.TriggerState.Paused : Models.TriggerState.Normal
            });
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
            && DateTime.TryParse(createdAtVal?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind,
                out var createdAt))
        {
            result.CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc).ToLocalTime();
        }

        if (jobDetail.JobDataMap.TryGetValue("UpdatedAt", out var updatedAtVal)
            && DateTime.TryParse(updatedAtVal?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind,
                out var updatedAt))
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

    private async Task<TriggerInfo?> MapToTriggerInfoAsync(ITrigger trigger, bool runOnStartup, CancellationToken ct)
    {
        var state = await scheduler.GetTriggerState(trigger.Key, ct);

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
                if (runOnStartup)
                {
                    triggerInfo.Type = TriggerType.OnStartup;
                }
                else
                {
                    triggerInfo.Type = TriggerType.Simple;
                    triggerInfo.RepeatCount = simpleTrigger.RepeatCount;
                    triggerInfo.RepeatInterval = simpleTrigger.RepeatInterval;
                }

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
