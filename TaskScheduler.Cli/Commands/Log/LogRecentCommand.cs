using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Log;

public sealed class LogRecentCommand(IExecutionLogService logService) : AsyncCommand<LogRecentCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-n|--count <COUNT>")]
        [Description("获取最近日志的条数（默认 20）")]
        [DefaultValue(20)]
        public int Count { get; set; } = 20;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var logs = await logService.GetRecentLogsAsync(Math.Max(1, settings.Count), cancellationToken);

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(logs);
            return 0;
        }

        var table = new Table()
            .Title($"[bold cyan]最近 {logs.Count} 条执行日志[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("任务名称")
            .AddColumn("开始时间")
            .AddColumn("状态")
            .AddColumn("耗时")
            .AddColumn("退出码")
            .AddColumn("输出/错误摘要");

        if (logs.Count == 0)
        {
            table.AddRow("—", "[grey]暂无执行日志[/]", "—", "—", "—", "—", "—");
        }
        else
        {
            foreach (var log in logs)
            {
                var summary = !string.IsNullOrWhiteSpace(log.Error)
                    ? log.Error.Replace("\r", " ").Replace("\n", " ")
                    : (log.Output ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                if (summary.Length > 55) summary = summary[..55] + "...";

                table.AddRow(
                    log.Id.ToString(),
                    Markup.Escape($"{log.JobName} ({log.JobGroup})"),
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
