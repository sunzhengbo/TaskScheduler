using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Dashboard;

public sealed class DashboardCommand(
    ITaskSchedulerService taskService,
    IExecutionLogService logService) : AsyncCommand<DashboardCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--upcoming-count <COUNT>")]
        [Description("显示的即将执行任务数量（默认 10）")]
        [DefaultValue(10)]
        public int UpcomingCount { get; set; } = 10;

        [CommandOption("--recent-count <COUNT>")]
        [Description("显示的最近活动记录数量（默认 10）")]
        [DefaultValue(10)]
        public int RecentCount { get; set; } = 10;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tasks = await taskService.GetAllTasksAsync(cancellationToken);
        var recentLogs = await logService.GetRecentLogsAsync(Math.Max(1, settings.RecentCount), cancellationToken);

        var totalCount = tasks.Count;
        var cronRunning = 0;
        var simpleRunning = 0;
        var startupRunning = 0;
        var runningCount = 0;
        var pausedCount = 0;
        var failedCount = 0;

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
                    else if (trigger.Type == TriggerType.OnStartup) startupRunning++;
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

            if (hasRunning) runningCount++;
            if (hasPaused) pausedCount++;
            if (hasFailed) failedCount++;
        }

        var upcomingList = tasks
            .SelectMany(t => t.Triggers
                .Where(tr => tr.State == TriggerState.Normal && tr.NextFireTime.HasValue)
                .Select(tr => new
                {
                    TaskName = t.Name,
                    TaskGroup = t.Group,
                    Description = t.Description ?? tr.TriggerDisplay,
                    TriggerType = tr.Type,
                    NextFireTime = tr.NextFireTime!.Value.ToLocalTime(),
                    Countdown = CliOutputFormatter.FormatCountdown(tr.NextFireTime!.Value),
                    HasError = t.Triggers.Any(x => x.State == TriggerState.Error)
                }))
            .OrderBy(x => x.NextFireTime)
            .Take(Math.Max(1, settings.UpcomingCount))
            .ToList();

        var isSchedulerActive = SchedulerBootstrapService.IsBackgroundSchedulerRunning();

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new
            {
                schedulerActive = isSchedulerActive,
                summary = new
                {
                    totalCount,
                    runningCount,
                    cronRunning,
                    simpleRunning,
                    startupRunning,
                    pausedCount,
                    failedCount
                },
                upcomingTasks = upcomingList,
                recentActivities = recentLogs.Select(l => new
                {
                    l.Id,
                    l.JobName,
                    l.JobGroup,
                    l.StartTime,
                    l.EndTime,
                    l.DurationMs,
                    l.DurationDisplay,
                    l.ExitCode,
                    l.Status,
                    l.Error
                })
            });
            return 0;
        }

        var engineBadge = isSchedulerActive
            ? "[green]● 调度引擎运行中[/]"
            : "[yellow]○ 调度引擎未常驻 (可执行 tscli daemon run 启动)[/]";
        AnsiConsole.MarkupLine($"[bold]TaskScheduler 仪表盘[/]  {engineBadge}");
        AnsiConsole.WriteLine();

        var runningParts = new List<string>();
        if (cronRunning > 0) runningParts.Add($"Cron {cronRunning}");
        if (simpleRunning > 0) runningParts.Add($"简单 {simpleRunning}");
        if (startupRunning > 0) runningParts.Add($"开机 {startupRunning}");
        var runningSub = runningParts.Count > 0 ? string.Join(" · ", runningParts) : "无";

        var statsTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]总任务数[/]").Centered())
            .AddColumn(new TableColumn("[bold green]运行中[/]").Centered())
            .AddColumn(new TableColumn("[bold yellow]已暂停[/]").Centered())
            .AddColumn(new TableColumn("[bold red]异常/错误[/]").Centered());

        statsTable.AddRow(
            $"[bold]{totalCount}[/]\n[grey]全部已注册任务[/]",
            $"[bold green]{runningCount}[/]\n[grey]{Markup.Escape(runningSub)}[/]",
            $"[bold yellow]{pausedCount}[/]\n[grey]共 {pausedCount} 个暂停[/]",
            $"[bold red]{failedCount}[/]\n[grey]触发器错误数[/]");

        AnsiConsole.Write(statsTable);
        AnsiConsole.WriteLine();

        var upcomingTable = new Table()
            .Title("[bold cyan]即将执行的任务[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("任务名称")
            .AddColumn("分组")
            .AddColumn("触发类型")
            .AddColumn("下次执行时间")
            .AddColumn("倒计时")
            .AddColumn("说明");

        if (upcomingList.Count == 0)
        {
            upcomingTable.AddRow("[grey]暂无即将执行的任务[/]", "—", "—", "—", "—", "—");
        }
        else
        {
            foreach (var item in upcomingList)
            {
                upcomingTable.AddRow(
                    Markup.Escape(item.TaskName),
                    Markup.Escape(item.TaskGroup),
                    Markup.Escape(item.TriggerType.ToString()),
                    item.NextFireTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    $"[cyan]{Markup.Escape(item.Countdown)}[/]",
                    Markup.Escape(item.Description));
            }
        }

        AnsiConsole.Write(upcomingTable);
        AnsiConsole.WriteLine();

        var recentTable = new Table()
            .Title("[bold cyan]最近执行活动[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("任务名称")
            .AddColumn("状态")
            .AddColumn("耗时")
            .AddColumn("发生时间")
            .AddColumn("摘要");

        if (recentLogs.Count == 0)
        {
            recentTable.AddRow("—", "[grey]暂无执行记录[/]", "—", "—", "—", "—");
        }
        else
        {
            foreach (var log in recentLogs)
            {
                var detail = log.Status == ExecutionStatus.Success
                    ? $"执行成功 ({log.DurationDisplay})"
                    : !string.IsNullOrWhiteSpace(log.Error)
                        ? Truncate(log.Error, 50)
                        : log.Status.ToString();

                recentTable.AddRow(
                    log.Id.ToString(),
                    Markup.Escape($"{log.JobName} ({log.JobGroup})"),
                    CliOutputFormatter.FormatExecutionStatusMarkup(log.Status),
                    Markup.Escape(log.DurationDisplay),
                    $"{log.StartTime:MM-dd HH:mm:ss} ({CliOutputFormatter.FormatTimeAgo(log.StartTime)})",
                    Markup.Escape(detail));
            }
        }

        AnsiConsole.Write(recentTable);
        return 0;
    }

    private static string Truncate(string text, int maxLength)
    {
        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }
}
