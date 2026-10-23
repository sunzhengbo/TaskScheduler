using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public class TaskSchedulerService(
    IScheduler scheduler,
    ILogger<TaskSchedulerService> logger,
    IIpcSyncService? ipcSyncService = null) : ITaskSchedulerService
{
    /// <summary>
    /// 将标准 5 字段 Unix cron 表达式转换为 Quartz.NET 6 字段格式。
    /// 若已是 6-7 字段则原样返回。
    /// </summary>
    public static string NormalizeCronExpression(string cronExpression)
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

    [DllImport("kernel32.dll")]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

    private static DateTimeOffset? GetWindowsBootTime(ILogger? logger = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            var query = new EventLogQuery("Microsoft-Windows-Kernel-Boot/Operational", PathType.LogName)
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record?.TimeCreated != null)
            {
                return DateTime.SpecifyKind(record.TimeCreated.Value, DateTimeKind.Local).ToUniversalTime();
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "读取 Windows 开机事件日志失败，降级使用 Uptime/TickCount 计算");
        }
        return null;
    }

    private static DateTimeOffset CalculateStartTime(bool useBootTime, TimeSpan interval, ILogger? logger = null)
    {
        if (!useBootTime)
            return DateTimeOffset.UtcNow.Add(interval);

        DateTimeOffset bootTime;
        var eventBootTime = GetWindowsBootTime(logger);
        if (eventBootTime.HasValue)
        {
            bootTime = eventBootTime.Value;
        }
        else
        {
            double uptimeSeconds;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && QueryUnbiasedInterruptTime(out ulong unbiasedTime))
            {
                uptimeSeconds = unbiasedTime / 10000000.0; // 100ns units to seconds
            }
            else
            {
                uptimeSeconds = Environment.TickCount64 / 1000.0;
            }

            bootTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(uptimeSeconds);
        }

        var elapsed = DateTimeOffset.UtcNow - bootTime;
        var intervalsNeeded = (long)Math.Ceiling(elapsed.TotalMilliseconds / interval.TotalMilliseconds);
        if (intervalsNeeded < 1) intervalsNeeded = 1;

        var result = bootTime + TimeSpan.FromTicks(interval.Ticks * intervalsNeeded);
        if (result <= DateTimeOffset.UtcNow)
            result += interval;

        return result;
    }

    private static string GetTriggerName(string jobName) => $"{jobName}_trigger";

    private static int CalculateRemainingCount(int repeatCount, int timesTriggered)
    {
        if (repeatCount == -1) return -1;
        return Math.Max(0, repeatCount - timesTriggered);
    }

    private Task NotifyTasksChangedAsync(string? jobName = null, string? jobGroup = null, CancellationToken ct = default)
    {
        return ipcSyncService != null
            ? ipcSyncService.NotifyAsync(SyncEventType.TasksChanged, jobName, jobGroup, ct: ct)
            : Task.CompletedTask;
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

        if (request.TriggerType == TriggerType.OnStartup)
        {
            jobDataMap["RunOnStartup"] = true.ToString();
        }

        var jobDetail = JobBuilder.Create(request.JobType)
            .WithIdentity(jobKey)
            .WithDescription(request.Description)
            .UsingJobData(jobDataMap)
            .StoreDurably()
            .Build();

        if (request.TriggerType == TriggerType.OnStartup)
        {
            await scheduler.AddJob(jobDetail, replace: true, ct).ConfigureAwait(false);
            await NotifyTasksChangedAsync(request.Name, request.Group, ct).ConfigureAwait(false);
            return $"{request.Group}.{request.Name}";
        }

        ITrigger trigger;
        var triggerKey = new TriggerKey(GetTriggerName(request.Name), request.Group);

        if (request.TriggerType == TriggerType.Simple)
        {
            var startTime = CalculateStartTime(request.UseBootTime, request.RepeatInterval, logger);
            trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .WithSimpleSchedule(x => x
                    .WithRepeatCount(request.RepeatCount)
                    .WithInterval(request.RepeatInterval)
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .StartAt(startTime)
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
                .StartAt(DateTimeOffset.UtcNow)
                .Build();
        }

        await scheduler.ScheduleJob(jobDetail, trigger, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(request.Name, request.Group, ct).ConfigureAwait(false);
        return $"{request.Group}.{request.Name}";
    }

    public async Task DeleteTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        await scheduler.DeleteJob(jobKey, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(jobName, jobGroup, ct).ConfigureAwait(false);
    }

    public async Task DeleteTaskById(string taskId, CancellationToken ct = default)
    {
        var (group, name) = ParseTaskId(taskId);
        await DeleteTaskAsync(name, group, ct).ConfigureAwait(false);
    }

    public async Task UpdateTaskAsync(string jobName, string jobGroup, TaskUpdateRequest request,
        CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
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

        await scheduler.AddJob(newJob, true, true, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(jobName, jobGroup, ct).ConfigureAwait(false);
    }

    public async Task<ScheduledTaskDetail?> GetTaskAsync(string jobName, string jobGroup,
        CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        return await GetTaskDetailAsync(jobKey, ct).ConfigureAwait(false);
    }

    public async Task<ScheduledTaskDetail?> GetTaskById(string taskId, CancellationToken ct = default)
    {
        var (group, name) = ParseTaskId(taskId);
        return await GetTaskAsync(name, group, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduledTaskDetail>> GetAllTasksAsync(CancellationToken ct = default)
    {
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct).ConfigureAwait(false);
        var tasks = new List<ScheduledTaskDetail>();

        foreach (var jobKey in jobKeys)
        {
            var task = await GetTaskDetailAsync(jobKey, ct).ConfigureAwait(false);
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
        var jobKeys = await scheduler.GetJobKeys(matcher, ct).ConfigureAwait(false);
        var tasks = new List<ScheduledTaskDetail>();

        foreach (var jobKey in jobKeys)
        {
            var task = await GetTaskDetailAsync(jobKey, ct).ConfigureAwait(false);
            if (task != null)
            {
                tasks.Add(task);
            }
        }

        return tasks;
    }

    public async Task<IReadOnlyList<string>> GetJobGroupsAsync(CancellationToken ct = default)
    {
        var groups = await scheduler.GetJobGroupNames(ct).ConfigureAwait(false);
        return groups.ToList();
    }

    public async Task PauseTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
        if (jobDetail == null) return;

        if (IsRunOnStartup(jobDetail))
        {
            jobDetail.JobDataMap["IsPaused"] = "true";
            await scheduler.AddJob(jobDetail, replace: true, ct).ConfigureAwait(false);
            await NotifyTasksChangedAsync(jobName, jobGroup, ct).ConfigureAwait(false);
            return;
        }

        await scheduler.PauseJob(jobKey, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(jobName, jobGroup, ct).ConfigureAwait(false);
    }

    public async Task ResumeTaskAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        var jobKey = new JobKey(jobName, jobGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
        if (jobDetail == null) return;

        if (IsRunOnStartup(jobDetail))
        {
            jobDetail.JobDataMap.Remove("IsPaused");
            await scheduler.AddJob(jobDetail, replace: true, ct).ConfigureAwait(false);
            await NotifyTasksChangedAsync(jobName, jobGroup, ct).ConfigureAwait(false);
            return;
        }

        await scheduler.ResumeJob(jobKey, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(jobName, jobGroup, ct).ConfigureAwait(false);
    }

    public async Task PauseTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        await scheduler.PauseTrigger(triggerKey, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(ct: ct).ConfigureAwait(false);
    }

    public async Task ResumeTriggerAsync(string triggerName, string triggerGroup, CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        await scheduler.ResumeTrigger(triggerKey, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(ct: ct).ConfigureAwait(false);
    }

    public async Task<bool> TriggerJobAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        if (ipcSyncService != null)
        {
            return await ipcSyncService.RequestTriggerJobNowAsync(jobName, jobGroup, ct).ConfigureAwait(false);
        }

        var jobKey = new JobKey(jobName, jobGroup);
        await scheduler.TriggerJob(jobKey, ct).ConfigureAwait(false);
        return true;
    }

    public async Task UpdateCronTriggerAsync(string triggerName, string triggerGroup, string cronExpression,
        CancellationToken ct = default)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        var oldTrigger = await scheduler.GetTrigger(triggerKey, ct).ConfigureAwait(false);
        var jobName = triggerName.EndsWith("_trigger", StringComparison.Ordinal)
            ? triggerName[..^"_trigger".Length]
            : triggerName;
        var jobKey = new JobKey(jobName, triggerGroup);

        var normalizedCron = NormalizeCronExpression(cronExpression);
        var builder = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithCronSchedule(normalizedCron, x => x
                .InTimeZone(TimeZoneInfo.Local)
                .WithMisfireHandlingInstructionFireAndProceed())
            .StartAt(DateTimeOffset.UtcNow);

        var newTrigger = builder.Build();

        if (oldTrigger != null)
        {
            PreservePreviousFireTime(oldTrigger, newTrigger);
            await scheduler.RescheduleJob(triggerKey, newTrigger, ct).ConfigureAwait(false);
        }
        else
        {
            await scheduler.ScheduleJob(newTrigger, ct).ConfigureAwait(false);
        }

        await NotifyTasksChangedAsync(jobName, triggerGroup, ct).ConfigureAwait(false);
    }

    public async Task UpdateSimpleTriggerAsync(string triggerName, string triggerGroup, int repeatCount,
        TimeSpan interval, CancellationToken ct = default, bool useBootTime = false)
    {
        var triggerKey = new TriggerKey(triggerName, triggerGroup);
        var oldTrigger = await scheduler.GetTrigger(triggerKey, ct).ConfigureAwait(false);
        var jobName = triggerName.EndsWith("_trigger", StringComparison.Ordinal)
            ? triggerName[..^"_trigger".Length]
            : triggerName;
        var jobKey = new JobKey(jobName, triggerGroup);

        var startTime = CalculateStartTime(useBootTime, interval, logger);
        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithSimpleSchedule(x => x
                .WithRepeatCount(repeatCount)
                .WithInterval(interval)
                .WithMisfireHandlingInstructionNextWithRemainingCount())
            .StartAt(startTime)
            .Build();

        if (useBootTime)
        {
            SetBootTimePreviousFireTime(newTrigger, startTime, interval);
        }
        else if (oldTrigger != null)
        {
            PreservePreviousFireTime(oldTrigger, newTrigger);
        }

        if (oldTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, newTrigger, ct).ConfigureAwait(false);
        }
        else
        {
            await scheduler.ScheduleJob(newTrigger, ct).ConfigureAwait(false);
        }

        await NotifyTasksChangedAsync(jobName, triggerGroup, ct).ConfigureAwait(false);
    }

    public async Task UpdateOnStartupTriggerAsync(string triggerName, string triggerGroup,
        CancellationToken ct = default)
    {
        var jobName = triggerName.EndsWith("_trigger", StringComparison.Ordinal)
            ? triggerName[..^"_trigger".Length]
            : triggerName;
        var jobKey = new JobKey(jobName, triggerGroup);
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
        if (jobDetail == null)
            throw new InvalidOperationException($"Job '{triggerGroup}.{jobName}' not found.");

        var triggers = await scheduler.GetTriggersOfJob(jobKey, ct).ConfigureAwait(false);
        foreach (var t in triggers)
        {
            await scheduler.UnscheduleJob(t.Key, ct).ConfigureAwait(false);
        }

        jobDetail.JobDataMap["RunOnStartup"] = true.ToString();
        var updatedJob = JobBuilder.Create(jobDetail.JobType)
            .WithIdentity(jobKey)
            .WithDescription(jobDetail.Description)
            .UsingJobData(jobDetail.JobDataMap)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(updatedJob, replace: true, ct).ConfigureAwait(false);
        await NotifyTasksChangedAsync(jobName, triggerGroup, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RescheduleAllSimpleTriggersAsync(CancellationToken ct = default)
    {
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct).ConfigureAwait(false);
        var count = 0;

        foreach (var jobKey in jobKeys)
        {
            var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
            if (jobDetail == null)
                continue;

            var triggers = await scheduler.GetTriggersOfJob(jobKey, ct).ConfigureAwait(false);
            foreach (var trigger in triggers)
            {
                if (trigger is not ISimpleTrigger simpleTrigger)
                    continue;

                var expectedName = GetTriggerName(jobKey.Name);
                if (trigger.Key.Name != expectedName)
                    continue;

                var useBootTime = false;
                if (jobDetail.JobDataMap.TryGetValue("UseBootTime", out var bootVal))
                {
                    useBootTime = bool.TryParse(bootVal?.ToString(), out var b) && b;
                }

                if (IsRunOnStartup(jobDetail))
                    continue;

                var interval = simpleTrigger.RepeatInterval;
                if (interval <= TimeSpan.Zero) continue;

                var originalState = await scheduler.GetTriggerState(trigger.Key, ct).ConfigureAwait(false);
                if (originalState == Quartz.TriggerState.Complete) continue;

                var startTime = CalculateStartTime(useBootTime, interval, logger);
                var remainingCount = CalculateRemainingCount(simpleTrigger.RepeatCount, simpleTrigger.TimesTriggered);

                var newTrigger = TriggerBuilder.Create()
                    .WithIdentity(trigger.Key)
                    .WithSimpleSchedule(x => x
                        .WithRepeatCount(remainingCount)
                        .WithInterval(interval)
                        .WithMisfireHandlingInstructionNextWithRemainingCount())
                    .StartAt(startTime)
                    .Build();

                if (useBootTime)
                {
                    SetBootTimePreviousFireTime(newTrigger, startTime, interval);
                }
                else
                {
                    PreservePreviousFireTime(trigger, newTrigger);
                }

                await scheduler.RescheduleJob(trigger.Key, newTrigger, ct).ConfigureAwait(false);
                count++;
                logger.LogInformation(
                    "重新调度 SimpleTrigger {TriggerKey}，UseBootTime={UseBootTime}，Interval={Interval}，StartTime={StartTime}",
                    trigger.Key, useBootTime, interval, startTime);
            }
        }

        logger.LogInformation("应用启动时重新调度了 {Count} 个 SimpleTrigger", count);
    }

    private static void SetBootTimePreviousFireTime(ITrigger newTrigger, DateTimeOffset startTime, TimeSpan interval)
    {
        if (newTrigger is AbstractTrigger abstractTrigger)
        {
            abstractTrigger.SetPreviousFireTimeUtc(startTime - interval);
        }
    }

    private static void PreservePreviousFireTime(ITrigger oldTrigger, ITrigger newTrigger)
    {
        var prevFireTime = oldTrigger.GetPreviousFireTimeUtc();
        if (!prevFireTime.HasValue) return;

        if (newTrigger is AbstractTrigger abstractTrigger)
        {
            abstractTrigger.SetPreviousFireTimeUtc(prevFireTime);
        }
    }

    private static bool IsRunOnStartup(IJobDetail jobDetail)
    {
        return jobDetail.JobDataMap.TryGetValue("RunOnStartup", out var val)
               && bool.TryParse(val?.ToString(), out var result) && result;
    }

    /// <inheritdoc />
    public async Task TriggerAllStartupTasksAsync(CancellationToken ct = default)
    {
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), ct).ConfigureAwait(false);
        var count = 0;

        foreach (var jobKey in jobKeys)
        {
            var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
            if (jobDetail == null) continue;

            if (!IsRunOnStartup(jobDetail))
                continue;

            if (jobDetail.JobDataMap.TryGetValue("IsPaused", out var pausedVal)
                && bool.TryParse(pausedVal?.ToString(), out var paused) && paused)
            {
                logger.LogInformation("跳过已暂停的开机启动任务 {JobKey}", jobKey);
                continue;
            }

            await scheduler.TriggerJob(jobKey, ct).ConfigureAwait(false);
            count++;
            logger.LogInformation("触发开机启动任务 {JobKey}", jobKey);
        }

        logger.LogInformation("应用启动时触发了 {Count} 个开机启动任务", count);
    }

    private async Task<ScheduledTaskDetail?> GetTaskDetailAsync(JobKey jobKey, CancellationToken ct)
    {
        var jobDetail = await scheduler.GetJobDetail(jobKey, ct).ConfigureAwait(false);
        if (jobDetail == null)
        {
            return null;
        }

        var triggers = await scheduler.GetTriggersOfJob(jobKey, ct).ConfigureAwait(false);
        var triggerInfos = new List<TriggerInfo>();

        var runOnStartup = IsRunOnStartup(jobDetail);

        var expectedTriggerName = GetTriggerName(jobKey.Name);
        foreach (var trigger in triggers)
        {
            if (trigger.Key.Name != expectedTriggerName)
                continue;

            var triggerInfo = await MapToTriggerInfoAsync(trigger, runOnStartup, ct).ConfigureAwait(false);
            if (triggerInfo != null)
            {
                triggerInfos.Add(triggerInfo);
            }
        }

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

        if (jobDetail.JobDataMap.TryGetValue("UseBootTime", out _))
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
        var state = await scheduler.GetTriggerState(trigger.Key, ct).ConfigureAwait(false);

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
