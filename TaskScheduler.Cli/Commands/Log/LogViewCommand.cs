using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Log;

public sealed class LogViewCommand(IExecutionLogService logService) : AsyncCommand<LogIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, LogIdSettings settings, CancellationToken cancellationToken)
    {
        var log = await logService.GetLogByIdAsync(settings.Id, cancellationToken);
        if (log == null)
        {
            CliOutputFormatter.PrintError($"未找到 ID 为 {settings.Id} 的执行日志", settings.Json);
            return 1;
        }

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(log);
            return 0;
        }

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(3))
            .AddColumn();

        grid.AddRow("[grey]日志 ID:[/]", log.Id.ToString());
        grid.AddRow("[grey]任务名称:[/]", $"[bold]{Markup.Escape(log.JobName)} ({Markup.Escape(log.JobGroup)})[/]");
        grid.AddRow("[grey]执行状态:[/]", CliOutputFormatter.FormatExecutionStatusMarkup(log.Status));
        grid.AddRow("[grey]开始时间:[/]", log.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
        grid.AddRow("[grey]结束时间:[/]", CliOutputFormatter.FormatDateTime(log.EndTime));
        grid.AddRow("[grey]执行耗时:[/]", $"[cyan]{Markup.Escape(log.DurationDisplay)}[/]");
        grid.AddRow("[grey]退出码:[/]", log.ExitCode?.ToString() ?? "—");

        AnsiConsole.Write(new Panel(grid)
            .Header($"[bold cyan]日志详情 #{log.Id} — {Markup.Escape(log.JobName)}[/]")
            .Border(BoxBorder.Rounded));

        if (!string.IsNullOrEmpty(log.Output))
        {
            AnsiConsole.Write(new Panel(Markup.Escape(log.Output.TrimEnd()))
                .Header("[bold green]=== 标准输出 ===[/]")
                .Border(BoxBorder.Rounded));
        }

        if (!string.IsNullOrEmpty(log.Error))
        {
            AnsiConsole.Write(new Panel($"[red]{Markup.Escape(log.Error.TrimEnd())}[/]")
                .Header("[bold red]=== 错误输出 ===[/]")
                .Border(BoxBorder.Rounded));
        }

        return 0;
    }
}
