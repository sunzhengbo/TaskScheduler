using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskRunCommand(
    ITaskSchedulerService taskService,
    ICommandExecutor commandExecutor,
    IExecutionLogService logService) : AsyncCommand<TaskRunCommand.Settings>
{
    public sealed class Settings : TaskTargetSettings
    {
        [CommandOption("--direct")]
        [Description("强制在当前 CLI 进程内直接同步执行任务命令并记录日志")]
        public bool Direct { get; set; }

        [CommandOption("--wait")]
        [Description("当交由后台调度器触发时，等待任务执行结束并打印输出结果")]
        public bool Wait { get; set; }

        [CommandOption("--timeout <SECONDS>")]
        [Description("等待后台调度器执行结果的最大超时秒数（默认 60 秒）")]
        [DefaultValue(60)]
        public int TimeoutSeconds { get; set; } = 60;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        var backgroundRunning = SchedulerBootstrapService.IsBackgroundSchedulerRunning();

        if (!settings.Direct && backgroundRunning)
        {
            var beforeLogs = await logService.GetLogsAsync(task.Name, task.Group, null, 1, 1, cancellationToken);
            var lastLogId = beforeLogs.FirstOrDefault()?.Id ?? 0;

            var triggeredViaIpc = await taskService.TriggerJobAsync(task.Name, task.Group, cancellationToken);
            if (!triggeredViaIpc)
            {
                CliOutputFormatter.PrintWarning("检测到后台调度器未响应实时 IPC 同步通道（可能运行的是未集成 IPC 的旧版本），将自动回退在当前进程内直接执行。");
            }
            else
            {
                if (!settings.Wait)
                {
                    CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已交由后台调度器触发执行", settings.Json);
                    return 0;
                }

                var timeoutSeconds = Math.Max(1, settings.TimeoutSeconds);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                ExecutionLog? matchedLog = null;

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await System.Threading.Tasks.Task.Delay(300, cts.Token);
                        var latest = await logService.GetLogsAsync(task.Name, task.Group, null, 1, 1, cts.Token);
                        if (latest.Count > 0 && latest[0].Id > lastLogId)
                        {
                            matchedLog = latest[0];
                            break;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常响应超时或用户 Ctrl+C 中断
                }

                if (matchedLog == null)
                {
                    var reason = cancellationToken.IsCancellationRequested
                        ? "已取消等待后台任务执行结果"
                        : $"等待后台任务执行结果超时（{timeoutSeconds} 秒）";
                    CliOutputFormatter.PrintError(reason, settings.Json);
                    return 1;
                }

                PrintExecutionResult(matchedLog, settings.Json);
                return matchedLog.Status == ExecutionStatus.Success ? 0 : 1;
            }
        }

        // 在当前 CLI 进程内直接执行命令并写入执行日志
        var command = CommandModel.DeserializeFromJson(task.CommandJson);
        if (command == null || string.IsNullOrWhiteSpace(command.Content))
        {
            CliOutputFormatter.PrintError($"任务 '{task.Group}.{task.Name}' 未配置有效命令内容", settings.Json);
            return 1;
        }

        var startTime = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var status = ExecutionStatus.Success;
        int? exitCode = null;
        string? output = null;
        string? error = null;

        try
        {
            var result = await commandExecutor.ExecuteCommandAsync(
                command.Content,
                command.Type,
                command.InterpreterPath,
                ct: cancellationToken);
            exitCode = result.ExitCode;
            output = result.Output;
            error = result.Error;

            if (result.ExitCode != 0)
            {
                status = ExecutionStatus.Failed;
                error = string.IsNullOrEmpty(result.Error)
                    ? $"退出码: {result.ExitCode}"
                    : $"{result.Error}\n退出码: {result.ExitCode}";
            }
        }
        catch (Exception ex)
        {
            status = ExecutionStatus.Failed;
            error = ex.Message;
        }

        stopwatch.Stop();

        var log = new ExecutionLog
        {
            JobName = task.Name,
            JobGroup = task.Group,
            StartTime = startTime,
            EndTime = DateTime.UtcNow,
            DurationMs = stopwatch.ElapsedMilliseconds,
            ExitCode = exitCode,
            Status = status,
            Output = !string.IsNullOrEmpty(output) ? output : null,
            Error = !string.IsNullOrEmpty(error) ? error : null
        };

        await logService.RecordExecutionAsync(log, CancellationToken.None);

        PrintExecutionResult(log, settings.Json);
        return status == ExecutionStatus.Success ? 0 : 1;
    }

    private static void PrintExecutionResult(ExecutionLog log, bool isJson)
    {
        if (isJson)
        {
            CliOutputFormatter.WriteJson(new
            {
                log.JobName,
                log.JobGroup,
                log.Status,
                log.ExitCode,
                log.DurationMs,
                log.DurationDisplay,
                StartTime = log.StartTime.ToLocalTime(),
                EndTime = log.EndTime?.ToLocalTime(),
                log.Output,
                log.Error
            });
            return;
        }

        AnsiConsole.MarkupLine($"任务: [bold]{Markup.Escape(log.JobGroup)}.{Markup.Escape(log.JobName)}[/]");
        AnsiConsole.MarkupLine($"状态: {CliOutputFormatter.FormatExecutionStatusMarkup(log.Status)}  |  耗时: [cyan]{Markup.Escape(log.DurationDisplay)}[/]  |  退出码: [bold]{log.ExitCode?.ToString() ?? "—"}[/]");

        if (!string.IsNullOrEmpty(log.Output))
        {
            var outPanel = new Panel(Markup.Escape(log.Output.TrimEnd()))
                .Header("[bold green]标准输出 (stdout)[/]")
                .Border(BoxBorder.Rounded);
            AnsiConsole.Write(outPanel);
        }

        if (!string.IsNullOrEmpty(log.Error))
        {
            var errPanel = new Panel($"[red]{Markup.Escape(log.Error.TrimEnd())}[/]")
                .Header("[bold red]错误输出 (stderr)[/]")
                .Border(BoxBorder.Rounded);
            AnsiConsole.Write(errPanel);
        }
    }
}
