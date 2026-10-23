using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskLogsCommand(
    ITaskSchedulerService taskService,
    IExecutionLogService logService) : AsyncCommand<TaskLogsCommand.Settings>
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

        [CommandOption("-n|--count <COUNT>")]
        [Description("显示的日志条数（默认 10）")]
        [DefaultValue(10)]
        public int Count { get; set; } = 10;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        var logs = await logService.GetLogsAsync(task.Name, task.Group, null, 1, Math.Max(1, settings.Count), cancellationToken);
        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(logs);
            return 0;
        }

        var table = new Table()
            .Title($"[bold cyan]任务执行历史 — {Markup.Escape(task.Group)}.{Markup.Escape(task.Name)} (最近 {logs.Count} 条)[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("日志 ID")
            .AddColumn("开始时间")
            .AddColumn("状态")
            .AddColumn("耗时")
            .AddColumn("退出码")
            .AddColumn("输出/错误摘要");

        if (logs.Count == 0)
        {
            table.AddRow("—", "[grey]暂无执行历史[/]", "—", "—", "—", "—");
        }
        else
        {
            foreach (var log in logs)
            {
                var summary = !string.IsNullOrWhiteSpace(log.Error)
                    ? log.Error.Replace("\r", " ").Replace("\n", " ")
                    : (log.Output ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                if (summary.Length > 60) summary = summary[..60] + "...";

                table.AddRow(
                    log.Id.ToString(),
                    log.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    CliOutputFormatter.FormatExecutionStatusMarkup(log.Status),
                    Markup.Escape(log.DurationDisplay),
                    log.ExitCode?.ToString() ?? "—",
                    Markup.Escape(string.IsNullOrWhiteSpace(summary) ? "—" : summary));
            }
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
