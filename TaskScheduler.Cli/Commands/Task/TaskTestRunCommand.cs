using System.ComponentModel;
using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskTestRunCommand(
    ICommandExecutor commandExecutor,
    ITaskSchedulerService taskService,
    IToolConfigService toolService,
    IInterpreterDetectionService? detectionService = null) : AsyncCommand<TaskTestRunCommand.Settings>
{
    private readonly IInterpreterDetectionService _detectionService = detectionService ?? InterpreterDetectionService.Default;
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-n|--name <TASK_NAME>")]
        [Description("测试已有任务的命令（不记录到历史执行日志）")]
        public string? TaskName { get; set; }

        [CommandOption("-g|--group <GROUP>")]
        [Description("已有任务所属分组（默认 DEFAULT）")]
        [DefaultValue("DEFAULT")]
        public string Group { get; set; } = "DEFAULT";

        [CommandOption("-t|--type <TYPE>")]
        [Description("命令类型（PowerShell, Python, Node.js, Bash, Cmd, VBScript，默认 PowerShell）")]
        public string? Type { get; set; }

        [CommandOption("-c|--cmd <CONTENT>")]
        [Description("要测试执行的命令或脚本内容")]
        public string? CommandContent { get; set; }

        [CommandOption("-f|--cmd-file <FILE>")]
        [Description("从脚本文件读取要测试执行的命令内容")]
        public string? CommandFile { get; set; }

        [CommandOption("--interpreter <PATH>")]
        [Description("可选指定解释器可执行文件路径或已配置版本名")]
        public string? Interpreter { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string cmdType;
        var cmdContent = settings.CommandContent;
        var interpreterPath = settings.Interpreter;

        if (!string.IsNullOrWhiteSpace(settings.TaskName))
        {
            var task = await taskService.GetTaskAsync(settings.TaskName, settings.Group, cancellationToken);
            if (task == null)
            {
                CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.TaskName}'", settings.Json);
                return 1;
            }
            var model = CommandModel.DeserializeFromJson(task.CommandJson);
            if (model == null)
            {
                CliOutputFormatter.PrintError($"任务 '{settings.Group}.{settings.TaskName}' 命令配置无效", settings.Json);
                return 1;
            }

            // 若显式传入了 -t/--type，优先使用覆盖的类型；否则默认采用任务自身的命令类型
            cmdType = !string.IsNullOrWhiteSpace(settings.Type)
                ? CommandTypes.Normalize(settings.Type)
                : model.Type;
            cmdContent ??= model.Content;
            interpreterPath ??= model.InterpreterVersion ?? model.InterpreterPath;
        }
        else
        {
            cmdType = CommandTypes.Normalize(settings.Type ?? "PowerShell");
            if (string.IsNullOrWhiteSpace(cmdContent) && !string.IsNullOrWhiteSpace(settings.CommandFile))
            {
                if (!File.Exists(settings.CommandFile))
                {
                    CliOutputFormatter.PrintError($"脚本文件不存在: {settings.CommandFile}", settings.Json);
                    return 2;
                }
                cmdContent = await File.ReadAllTextAsync(settings.CommandFile, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(interpreterPath))
            {
                var allTools = await toolService.GetAllToolsAsync(cancellationToken);
                interpreterPath = TaskCreateCommand.ResolveInterpreterVersion(cmdType, null, allTools);
            }
        }

        if (string.IsNullOrWhiteSpace(cmdContent))
        {
            CliOutputFormatter.PrintError("请提供要测试的命令内容（--cmd、--cmd-file 或 --name <已有任务名>）", settings.Json);
            return 2;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await commandExecutor.ExecuteCommandAsync(
                cmdContent,
                cmdType,
                interpreterPath,
                ct: cancellationToken);
            sw.Stop();

            if (settings.Json)
            {
                CliOutputFormatter.WriteJson(new
                {
                    type = cmdType,
                    interpreterPath = interpreterPath ?? await _detectionService.DetectAsync(cmdType, ct: cancellationToken),
                    result.ExitCode,
                    durationMs = sw.ElapsedMilliseconds,
                    result.Output,
                    result.Error
                });
                return result.ExitCode == 0 ? 0 : 1;
            }

            var statusColor = result.ExitCode == 0 ? "green" : "red";
            AnsiConsole.MarkupLine($"[{statusColor}]退出码: {result.ExitCode}[/]  |  类型: [cyan]{Markup.Escape(cmdType)}[/]  |  耗时: [cyan]{sw.ElapsedMilliseconds}ms[/]");

            if (!string.IsNullOrEmpty(result.Output))
            {
                AnsiConsole.Write(new Panel(Markup.Escape(result.Output.TrimEnd()))
                    .Header("[bold green]输出 (Output)[/]")
                    .Border(BoxBorder.Rounded));
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                AnsiConsole.Write(new Panel($"[red]{Markup.Escape(result.Error.TrimEnd())}[/]")
                    .Header("[bold red]错误 (Error)[/]")
                    .Border(BoxBorder.Rounded));
            }

            return result.ExitCode == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            CliOutputFormatter.PrintError($"执行失败: {ex.Message}", settings.Json);
            return 1;
        }
    }
}
