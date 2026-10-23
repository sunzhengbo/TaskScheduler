using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskDeleteCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskDeleteCommand.Settings>
{
    public sealed class Settings : TaskTargetSettings
    {
        [CommandOption("-y|--yes")]
        [Description("跳过二次确认提示直接删除任务")]
        public bool ConfirmYes { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        if (!settings.ConfirmYes)
        {
            if (settings.Json || Console.IsInputRedirected)
            {
                CliOutputFormatter.PrintError("此操作不可撤销，如需确认执行请添加 -y/--yes 参数", settings.Json);
                return 1;
            }

            var confirmed = AnsiConsole.Confirm($"确定要删除任务 \"{task.Group}.{task.Name}\" 吗？此操作不可撤销。", false);
            if (!confirmed)
            {
                CliOutputFormatter.PrintInfo("已取消删除操作");
                return 0;
            }
        }

        await taskService.DeleteTaskAsync(task.Name, task.Group, cancellationToken);
        CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已删除", settings.Json);
        return 0;
    }
}
