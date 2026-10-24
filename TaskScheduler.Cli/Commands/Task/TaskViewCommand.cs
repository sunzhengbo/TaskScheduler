using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskViewCommand(
    ITaskSchedulerService taskService,
    IExecutionLogService logService,
    IToolConfigService? toolConfigService = null,
    IInterpreterDetectionService? detectionService = null) : AsyncCommand<TaskViewCommand.Settings>
{
    private readonly IInterpreterDetectionService _detectionService = detectionService ?? InterpreterDetectionService.Default;
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("任务名称")]
        public string Name { get; set; } = string.Empty;

        [CommandOption("-g|--group <GROUP>")]
        [Description("任务分组（默认 DEFAULT）")]
        [DefaultValue("DEFAULT")]
        public string Group { get; set; } = "DEFAULT";

        [CommandOption("--log-count <COUNT>")]
        [Description("显示的最近执行日志条数（默认 10）")]
        [DefaultValue(10)]
        public int LogCount { get; set; } = 10;
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
        var recentLogs = await logService.GetLogsAsync(task.Name, task.Group, null, 1, Math.Max(1, settings.LogCount), cancellationToken);
        var command = CommandModel.DeserializeFromJson(task.CommandJson);
        var tr = task.FirstTrigger;

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new
            {
                task = new
                {
                    task.Name,
                    task.Group,
                    task.Description,
                    task.Priority,
                    task.IsEnabled,
                    PrimaryState = tr?.State ?? TriggerState.None,
                    LastExecutionStatus = task.LastExecutionStatus?.ToString(),
                    task.LastExecutionDurationMs,
                    task.LastExecutionError,
                    ScheduleDisplay = tr?.TriggerDisplay ?? "—",
                    task.UseBootTime,
                    RunOnStartup = tr?.Type == TriggerType.OnStartup,
                    NextFireTime = tr?.NextFireTime?.ToLocalTime(),
                    PreviousFireTime = tr?.PreviousFireTime?.ToLocalTime(),
                    CreatedAt = task.CreatedAt?.ToLocalTime(),
                    UpdatedAt = task.UpdatedAt?.ToLocalTime(),
                    task.Triggers
                },
                command,
                statistics = stats,
                recentLogs
            });
            return 0;
        }

        var metaGrid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(3))
            .AddColumn();

        metaGrid.AddRow("[grey]任务名称:[/]", $"[bold]{Markup.Escape(task.Name)}[/]");
        metaGrid.AddRow("[grey]所属分组:[/]", Markup.Escape(task.Group));
        metaGrid.AddRow("[grey]当前状态:[/]", CliOutputFormatter.FormatTriggerStateMarkup(tr?.State ?? TriggerState.None));

        var lastResultMarkup = task.LastExecutionStatus.HasValue
            ? CliOutputFormatter.FormatExecutionStatusMarkup(task.LastExecutionStatus.Value)
            : "[grey]未执行[/]";
        if (!string.IsNullOrWhiteSpace(task.LastExecutionError))
        {
            var firstLineError = task.LastExecutionError.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (firstLineError.Length > 40) firstLineError = firstLineError[..37] + "...";
            lastResultMarkup += $" [red]({Markup.Escape(firstLineError)})[/]";
        }
        metaGrid.AddRow("[grey]上次执行结果:[/]", lastResultMarkup);

        metaGrid.AddRow("[grey]优先级别:[/]", CliOutputFormatter.FormatPriorityMarkup(task.Priority));
        metaGrid.AddRow("[grey]调度规则:[/]", Markup.Escape(tr?.TriggerDisplay ?? "—"));
        metaGrid.AddRow("[grey]开机时间锚点:[/]", task.UseBootTime ? "是 (系统开机模式)" : "否 (应用启动模式)");
        metaGrid.AddRow("[grey]下次执行时间:[/]", CliOutputFormatter.FormatDateTimeOffset(tr?.NextFireTime));
        metaGrid.AddRow("[grey]上次执行时间:[/]", CliOutputFormatter.FormatDateTimeOffset(tr?.PreviousFireTime));
        metaGrid.AddRow("[grey]创建时间:[/]", CliOutputFormatter.FormatDateTime(task.CreatedAt));
        metaGrid.AddRow("[grey]更新时间:[/]", CliOutputFormatter.FormatDateTime(task.UpdatedAt));
        metaGrid.AddRow("[grey]任务描述:[/]", Markup.Escape(task.Description ?? "—"));

        var metaPanel = new Panel(metaGrid)
            .Header($"[bold cyan]任务详情 — {Markup.Escape(task.Group)}.{Markup.Escape(task.Name)}[/]")
            .Border(BoxBorder.Rounded);
        AnsiConsole.Write(metaPanel);

        if (command != null)
        {
            var cmdGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(3))
                .AddColumn();
            cmdGrid.AddRow("[grey]命令名称:[/]", Markup.Escape(command.Name));
            cmdGrid.AddRow("[grey]命令类型:[/]", $"[cyan]{Markup.Escape(command.Type)}[/]");
            var interpreterDisplay = await FormatInterpreterDisplayAsync(command, cancellationToken);
            cmdGrid.AddRow("[grey]解释器配置:[/]", interpreterDisplay);
            cmdGrid.AddRow("[grey]脚本内容:[/]", $"[white]{Markup.Escape(command.Content)}[/]");

            var cmdPanel = new Panel(cmdGrid)
                .Header("[bold cyan]命令配置[/]")
                .Border(BoxBorder.Rounded);
            AnsiConsole.Write(cmdPanel);
        }

        var statsTable = new Table()
            .Title("[bold cyan]执行统计[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("累计执行")
            .AddColumn("成功次数")
            .AddColumn("失败次数")
            .AddColumn("成功率")
            .AddColumn("平均耗时")
            .AddColumn("连续失败");

        statsTable.AddRow(
            stats.TotalExecutions.ToString(),
            $"[green]{stats.SuccessCount}[/]",
            $"[red]{stats.FailCount}[/]",
            $"{stats.SuccessRate:F1}%",
            Markup.Escape(stats.AverageDurationDisplay),
            stats.ConsecutiveFailures > 0 ? $"[red]{stats.ConsecutiveFailures}[/]" : "0");
        AnsiConsole.Write(statsTable);

        var logTable = new Table()
            .Title($"[bold cyan]最近 {recentLogs.Count} 条执行记录[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("日志 ID")
            .AddColumn("开始时间")
            .AddColumn("状态")
            .AddColumn("耗时")
            .AddColumn("退出码")
            .AddColumn("输出/错误摘要");

        if (recentLogs.Count == 0)
        {
            logTable.AddRow("—", "[grey]暂无执行历史[/]", "—", "—", "—", "—");
        }
        else
        {
            foreach (var log in recentLogs)
            {
                var summary = !string.IsNullOrWhiteSpace(log.Error)
                    ? log.Error.Replace("\r", " ").Replace("\n", " ")
                    : (log.Output ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                if (summary.Length > 60) summary = summary[..60] + "...";

                logTable.AddRow(
                    log.Id.ToString(),
                    log.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    CliOutputFormatter.FormatExecutionStatusMarkup(log.Status),
                    Markup.Escape(log.DurationDisplay),
                    log.ExitCode?.ToString() ?? "—",
                    Markup.Escape(string.IsNullOrWhiteSpace(summary) ? "—" : summary));
            }
        }

        AnsiConsole.Write(logTable);
        return 0;
    }

    private async Task<string> FormatInterpreterDisplayAsync(CommandModel command, CancellationToken ct)
    {
        var rawVersion = command.InterpreterVersion;
        var toolType = CommandTypes.MapToToolType(command.Type);

        if (toolType == null)
        {
            var detectedBuiltIn = await _detectionService.DetectAsync(command.Type, ct: ct);
            return Markup.Escape(rawVersion ?? detectedBuiltIn ?? "系统默认 (PATH)");
        }

        IReadOnlyList<ToolConfig>? tools = null;
        if (toolConfigService != null)
        {
            try
            {
                tools = await toolConfigService.GetToolsByTypeAsync(toolType, ct);
            }
            catch
            {
                // 忽略异常
            }
        }

        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            var defTool = tools?.FirstOrDefault(t => t.IsDefault) ?? tools?.FirstOrDefault();
            if (defTool != null)
            {
                return $"[grey]未指定 (自动使用全局默认: {Markup.Escape(defTool.VersionName)})[/]";
            }
            return "[grey]系统默认 (PATH)[/]";
        }

        var candidateName = command.InterpreterName ?? rawVersion.Trim();
        var matched = tools?.FirstOrDefault(t => t.VersionName.Equals(candidateName, StringComparison.OrdinalIgnoreCase));

        if (matched != null)
        {
            return $"[green]{Markup.Escape(candidateName)}[/] [grey]({Markup.Escape(matched.ExecutablePath)})[/]";
        }

        // 未在工具库中注册
        var defaultTool = tools?.FirstOrDefault(t => t.IsDefault) ?? tools?.FirstOrDefault();
        if (defaultTool != null)
        {
            return $"[yellow]{Markup.Escape(rawVersion)}[/] [bold yellow](⚠️ 未在工具库中注册，运行时将自动回退到默认: {Markup.Escape(defaultTool.VersionName)})[/]";
        }

        return $"[yellow]{Markup.Escape(rawVersion)}[/] [bold yellow](⚠️ 工具库未配置该语言工具，运行时将从系统 PATH 查找)[/]";
    }
}
