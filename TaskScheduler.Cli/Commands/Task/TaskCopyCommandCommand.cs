using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskCopyCommandCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskCopyCommandCommand.Settings>
{
    public sealed class Settings : TaskTargetSettings
    {
        [CommandOption("-n|--no-clipboard")]
        [Description("仅将命令脚本输出到控制台，不复制到系统剪贴板")]
        public bool NoClipboard { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        var command = CommandModel.DeserializeFromJson(task.CommandJson);
        if (command == null || string.IsNullOrWhiteSpace(command.Content))
        {
            CliOutputFormatter.PrintError($"任务 '{task.Group}.{task.Name}' 未配置有效命令脚本", settings.Json);
            return 1;
        }

        var copiedToClipboard = false;
        if (!settings.NoClipboard)
        {
            var ok = await ClipboardHelper.SetTextAsync(command.Content, cancellationToken);
            if (!ok)
            {
                CliOutputFormatter.PrintError("未能成功写入系统剪贴板", settings.Json);
                return 1;
            }
            copiedToClipboard = true;
        }

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new
            {
                taskName = task.Name,
                taskGroup = task.Group,
                commandType = command.Type,
                commandContent = command.Content,
                copiedToClipboard = copiedToClipboard
            });
            return 0;
        }

        if (copiedToClipboard)
        {
            CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 的命令脚本内容已复制到系统剪贴板");
        }

        AnsiConsole.WriteLine(command.Content);
        return 0;
    }
}
