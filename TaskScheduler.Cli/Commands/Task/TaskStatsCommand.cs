using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskStatsCommand(
    ITaskSchedulerService taskService,
    IExecutionLogService logService) : AsyncCommand<TaskStatsCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("任务名称")]
        public string Name { get; set; } = string.Empty;

        [CommandOption("-g|--group <GROUP>")]
        [Description("任务分组（默认 DEFAULT）")]
        [DefaultValue("DEFAULT")]
        public string Group { get; set; } = "DEFAULT";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        var stats = await logService.GetStatisticsAsync(task.Name, task.Group, cancellationToken);
        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(stats);
            return 0;
        }

        var table = new Table()
            .Title($"[bold cyan]任务统计 — {Markup.Escape(task.Group)}.{Markup.Escape(task.Name)}[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("总执行次数")
            .AddColumn("成功")
            .AddColumn("失败")
            .AddColumn("成功率")
            .AddColumn("平均耗时")
            .AddColumn("最近执行时间")
            .AddColumn("最近状态")
            .AddColumn("连续失败");

        table.AddRow(
            stats.TotalExecutions.ToString(),
            $"[green]{stats.SuccessCount}[/]",
            $"[red]{stats.FailCount}[/]",
            $"{stats.SuccessRate:F1}%",
            Markup.Escape(stats.AverageDurationDisplay),
            CliOutputFormatter.FormatDateTime(stats.LastExecutionTime),
            stats.LastStatus.HasValue ? CliOutputFormatter.FormatExecutionStatusMarkup(stats.LastStatus.Value) : "—",
            stats.ConsecutiveFailures.ToString());

        AnsiConsole.Write(table);
        return 0;
    }
}
