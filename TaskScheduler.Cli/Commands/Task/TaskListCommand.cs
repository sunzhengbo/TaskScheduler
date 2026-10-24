using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskListCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskListCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-g|--group <GROUP>")]
        [Description("按任务分组筛选（如 DEFAULT, SYSTEM, USER, DAEMON）")]
        public string? Group { get; set; }

        [CommandOption("-s|--state <STATE>")]
        [Description("按触发器状态筛选（Normal, Paused, Complete, Blocked, Error, None）")]
        public TriggerState? State { get; set; }

        [CommandOption("-q|--search <KEYWORD>")]
        [Description("按任务名称、分组或描述关键词搜索")]
        public string? Search { get; set; }

        [CommandOption("--upcoming")]
        [Description("仅显示即将执行的运行中任务，并按下次触发时间排序")]
        public bool UpcomingOnly { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tasks = !string.IsNullOrWhiteSpace(settings.Group)
            ? await taskService.GetTasksByGroupAsync(settings.Group, cancellationToken)
            : await taskService.GetAllTasksAsync(cancellationToken);

        var filtered = tasks.AsEnumerable();

        if (settings.State.HasValue)
        {
            var targetState = settings.State.Value;
            filtered = filtered.Where(t => t.Triggers.Any(tr => tr.State == targetState));
        }

        if (!string.IsNullOrWhiteSpace(settings.Search))
        {
            var kw = settings.Search.Trim();
            filtered = filtered.Where(t =>
                t.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                t.Group.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (t.Description?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (settings.UpcomingOnly)
        {
            filtered = filtered
                .Where(t => t.FirstTrigger is { State: TriggerState.Normal, NextFireTime: not null })
                .OrderBy(t => t.FirstTrigger!.NextFireTime);
        }

        var resultList = filtered.ToList();

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(resultList.Select(t => new
            {
                t.Name,
                t.Group,
                t.Description,
                t.Priority,
                t.IsEnabled,
                PrimaryState = t.FirstTrigger?.State ?? TriggerState.None,
                LastExecutionStatus = t.LastExecutionStatus?.ToString(),
                t.LastExecutionDurationMs,
                t.LastExecutionError,
                ScheduleDisplay = t.FirstTrigger?.TriggerDisplay ?? "—",
                t.UseBootTime,
                RunOnStartup = t.FirstTrigger?.Type == TriggerType.OnStartup,
                NextFireTime = t.FirstTrigger?.NextFireTime?.ToLocalTime(),
                PreviousFireTime = t.FirstTrigger?.PreviousFireTime?.ToLocalTime(),
                CreatedAt = t.CreatedAt?.ToLocalTime(),
                UpdatedAt = t.UpdatedAt?.ToLocalTime(),
                Command = CommandModel.DeserializeFromJson(t.CommandJson),
                t.Triggers
            }));
            return 0;
        }

        var table = new Table()
            .Title($"[bold cyan]定时任务列表 (共 {resultList.Count} 项)[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("任务名称")
            .AddColumn("分组")
            .AddColumn("状态")
            .AddColumn("上次结果")
            .AddColumn("调度规则")
            .AddColumn("优先级")
            .AddColumn("下次执行时间")
            .AddColumn("上次执行时间")
            .AddColumn("描述");

        if (resultList.Count == 0)
        {
            table.AddRow("[grey]无匹配任务[/]", "—", "—", "—", "—", "—", "—", "—", "—");
        }
        else
        {
            foreach (var task in resultList)
            {
                var tr = task.FirstTrigger;
                var lastResultMarkup = task.LastExecutionStatus.HasValue
                    ? CliOutputFormatter.FormatExecutionStatusMarkup(task.LastExecutionStatus.Value)
                    : "[grey]未执行[/]";

                table.AddRow(
                    $"[bold]{Markup.Escape(task.Name)}[/]",
                    Markup.Escape(task.Group),
                    CliOutputFormatter.FormatTriggerStateMarkup(tr?.State ?? TriggerState.None),
                    lastResultMarkup,
                    Markup.Escape(tr?.TriggerDisplay ?? "—"),
                    CliOutputFormatter.FormatPriorityMarkup(task.Priority),
                    CliOutputFormatter.FormatDateTimeOffset(tr?.NextFireTime),
                    CliOutputFormatter.FormatDateTimeOffset(tr?.PreviousFireTime),
                    Markup.Escape(task.Description ?? "—"));
            }
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
