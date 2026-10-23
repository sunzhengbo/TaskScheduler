using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskToggleCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskTargetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TaskTargetSettings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        if (task.IsEnabled)
        {
            await taskService.PauseTaskAsync(task.Name, task.Group, cancellationToken);
            CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已切换为【暂停】状态", settings.Json);
        }
        else
        {
            await taskService.ResumeTaskAsync(task.Name, task.Group, cancellationToken);
            CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已切换为【启用】状态", settings.Json);
        }
        return 0;
    }
}
