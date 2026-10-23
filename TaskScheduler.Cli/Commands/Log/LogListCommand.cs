using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Log;

public sealed class LogListCommand(IExecutionLogService logService) : AsyncCommand<LogListCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--page <PAGE>")]
        [Description("页码（从 1 开始，默认 1）")]
        [DefaultValue(1)]
        public int Page { get; set; } = 1;

        [CommandOption("--size <SIZE>")]
        [Description("每页记录数（默认 50）")]
        [DefaultValue(50)]
        public int PageSize { get; set; } = 50;

        [CommandOption("-s|--status <STATUS>")]
        [Description("按执行状态过滤（Success, Failed, Timeout, Cancelled）")]
        public string? Status { get; set; }

        [CommandOption("-j|--job <NAME>")]
        [Description("按任务名称过滤")]
        public string? JobName { get; set; }

        [CommandOption("-g|--group <GROUP>")]
        [Description("按任务分组过滤")]
        public string? JobGroup { get; set; }

        [CommandOption("-q|--search <KEYWORD>")]
        [Description("按任务名称、输出或错误信息关键词搜索")]
        public string? Search { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, settings.Page);
        var pageSize = Math.Max(1, settings.PageSize);
        string? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(settings.Status) && settings.Status != "全部")
        {
            if (Enum.TryParse<ExecutionStatus>(settings.Status.Trim(), true, out var parsedStatus))
            {
                statusFilter = parsedStatus.ToString();
            }
            else
            {
                CliOutputFormatter.PrintError($"无效的执行状态: '{settings.Status}'。支持的状态: Success, Failed, Timeout, Cancelled", settings.Json);
                return 2;
            }
        }

        var logs = await logService.GetLogsAsync(
            settings.JobName,
            settings.JobGroup,
            statusFilter,
            page,
            pageSize,
            cancellationToken,
            settings.Search);

        var totalCount = await logService.GetLogCountAsync(
            settings.JobName,
            settings.JobGroup,
            statusFilter,
            cancellationToken,
            settings.Search);

        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize));

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new
            {
                page,
                pageSize,
                totalCount,
                totalPages,
                items = logs
            });
            return 0;
        }

        var table = new Table()
            .Title($"[bold cyan]执行日志列表 (第 {page}/{totalPages} 页，共 {totalCount} 条)[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("任务名称")
            .AddColumn("分组")
            .AddColumn("开始时间")
            .AddColumn("状态")
            .AddColumn("耗时")
            .AddColumn("退出码")
            .AddColumn("输出/错误摘要");

        if (logs.Count == 0)
        {
            table.AddRow("—", "[grey]暂无符合条件的执行日志[/]", "—", "—", "—", "—", "—", "—");
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
                    Markup.Escape(log.JobName),
                    Markup.Escape(log.JobGroup),
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
