using System;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

using Avalonia.Controls.Notifications;

using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.ViewModels;

/// <summary>
/// 仪表盘中即将执行的任务项
/// </summary>
public class UpcomingTaskItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    /// <summary>Normal, Error</summary>
    public string Status { get; set; } = "Normal";
}

/// <summary>
/// 仪表盘中最近活动项
/// </summary>
public class RecentActivityItem
{
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
    public string Status { get; set; } = "Success";
}

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ITaskSchedulerService _taskService;
    private readonly IExecutionLogService _logService;
    private readonly INavigationService _navigation;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _pausedCount;
    [ObservableProperty] private int _failedCount;

    /// <summary>运行中卡片的副标题</summary>
    [ObservableProperty] private string _runningSub = string.Empty;

    /// <summary>已暂停卡片的副标题</summary>
    [ObservableProperty] private string _pausedSub = string.Empty;

    /// <summary>失败卡片的副标题</summary>
    [ObservableProperty] private string _failedSub = string.Empty;

    [ObservableProperty] private AvaloniaList<UpcomingTaskItem> _upcomingTasks = new();
    [ObservableProperty] private AvaloniaList<RecentActivityItem> _recentActivities = new();
    [ObservableProperty] private bool _hasUpcomingTasks;
    [ObservableProperty] private bool _hasRecentActivities;
    [ObservableProperty] private bool _isLoading = true;

    public DashboardViewModel(ITaskSchedulerService taskService, IExecutionLogService logService, INavigationService navigation, ILogger<DashboardViewModel> logger)
    {
        _taskService = taskService;
        _logService = logService;
        _navigation = navigation;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var tasks = await _taskService.GetAllTasksAsync();

            TotalCount = tasks.Count;
            var cronRunning = 0;
            var simpleRunning = 0;
            RunningCount = 0;
            PausedCount = 0;
            FailedCount = 0;

            string? failedTaskName = null;

            foreach (var t in tasks)
            {
                var hasRunning = false;
                var hasPaused = false;
                var hasFailed = false;

                foreach (var trigger in t.Triggers)
                {
                    if (trigger.State == TriggerState.Normal)
                    {
                        hasRunning = true;
                        if (trigger.Type == TriggerType.Cron) cronRunning++;
                        else simpleRunning++;
                    }
                    else if (trigger.State == TriggerState.Paused)
                    {
                        hasPaused = true;
                    }
                    else if (trigger.State == TriggerState.Error)
                    {
                        hasFailed = true;
                    }
                }

                if (hasRunning) RunningCount++;
                if (hasPaused) PausedCount++;
                if (hasFailed)
                {
                    FailedCount++;
                    failedTaskName ??= t.Name;
                }
            }

            // 统计卡片副标题
            var runningParts = new System.Collections.Generic.List<string>();
            if (cronRunning > 0) runningParts.Add($"Cron {cronRunning}");
            if (simpleRunning > 0) runningParts.Add($"简单 {simpleRunning}");
            RunningSub = runningParts.Count > 0 ? string.Join(" · ", runningParts) : "无";

            PausedSub = PausedCount > 0 ? $"共 {PausedCount} 个任务" : "无";

            if (FailedCount > 0 && failedTaskName != null)
            {
                // 尝试获取最近失败日志的时间
                var recentLogs = await _logService.GetRecentLogsAsync(1);
                var failedLog = recentLogs.FirstOrDefault(l => l.Status == ExecutionStatus.Failed);
                var timeText = failedLog != null ? FormatTimeAgo(failedLog.StartTime) : "最近";
                FailedSub = $"{failedTaskName} · {timeText}";
            }
            else
            {
                FailedSub = "无";
            }

            // 即将执行的任务：取所有 Normal 状态触发器中 NextFireTime 最近的任务
            var upcoming = new AvaloniaList<UpcomingTaskItem>();
            var upcomingList = tasks
                .SelectMany(t => t.Triggers
                    .Where(tr => tr.State == TriggerState.Normal && tr.NextFireTime.HasValue)
                    .Select(tr => new { Task = t, Trigger = tr }))
                .OrderBy(x => x.Trigger.NextFireTime)
                .Take(5)
                .ToList();

            foreach (var item in upcomingList)
            {
                var nextFire = item.Trigger.NextFireTime!.Value;
                var status = item.Task.Triggers.Any(tr => tr.State == TriggerState.Error) ? "Error" : "Normal";
                upcoming.Add(new UpcomingTaskItem
                {
                    Name = item.Task.Name,
                    Description = item.Task.Description ?? item.Trigger.TriggerDisplay,
                    TimeText = FormatNextFireTime(nextFire),
                    Status = status
                });
            }

            UpcomingTasks = upcoming;
            HasUpcomingTasks = upcoming.Count > 0;

            // 最近活动：从执行日志获取
            var activities = new AvaloniaList<RecentActivityItem>();
            var logs = await _logService.GetRecentLogsAsync(5);

            foreach (var log in logs)
            {
                var detail = log.Status == ExecutionStatus.Success
                    ? $"执行成功，耗时 {log.DurationDisplay}"
                    : !string.IsNullOrWhiteSpace(log.Error)
                        ? TruncateString(log.Error, 40)
                        : log.Status.ToString();

                activities.Add(new RecentActivityItem
                {
                    Name = log.JobName,
                    Detail = detail,
                    TimeAgo = FormatTimeAgo(log.StartTime),
                    Status = log.Status.ToString()
                });
            }

            RecentActivities = activities;
            HasRecentActivities = activities.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载仪表盘数据失败");
            ShowToast("加载仪表盘数据失败", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void NavigateToTaskList()
    {
        _navigation.NavigateTo<TaskListViewModel>();
    }

    [RelayCommand]
    private void NavigateToExecutionLog()
    {
        _navigation.NavigateTo<ExecutionLogViewModel>();
    }

    private static string FormatTimeAgo(DateTime time)
    {
        var utcTime = time.Kind == DateTimeKind.Utc
            ? time
            : time.Kind == DateTimeKind.Local
                ? time.ToUniversalTime()
                : DateTime.SpecifyKind(time, DateTimeKind.Utc);
        var diff = DateTime.UtcNow - utcTime;
        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        return $"{(int)diff.TotalDays} 天前";
    }

    private static string FormatNextFireTime(DateTimeOffset nextFire)
    {
        var local = nextFire.LocalDateTime;
        var now = DateTime.Now;
        if (local.Date == now.Date)
            return local.ToString("HH:mm");
        if (local.Date == now.Date.AddDays(1))
            return $"明天 {local:HH:mm}";
        return local.ToString("MM/dd HH:mm");
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
    }
}
