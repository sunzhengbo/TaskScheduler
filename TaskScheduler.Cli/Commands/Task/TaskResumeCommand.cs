using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskResumeCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskTargetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TaskTargetSettings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        await taskService.ResumeTaskAsync(task.Name, task.Group, cancellationToken);
        CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已恢复运行", settings.Json);
        return 0;
    }
}
