using System.ComponentModel;
using System.Text;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskExportCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskExportCommand.Settings>
{
    public sealed class Settings : TaskTargetSettings
    {
        [CommandOption("-o|--output <FILE>")]
        [Description("导出到指定的 JSON 文件路径")]
        public string? OutputFile { get; set; }

        [CommandOption("-c|--clipboard")]
        [Description("将导出的任务 JSON 复制到系统剪贴板")]
        public bool Clipboard { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var task = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (task == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        var exportModel = TaskExportModel.FromTask(task, task.CommandJson);
        var json = exportModel.ToJson();

        if (!string.IsNullOrWhiteSpace(settings.OutputFile))
        {
            DirectoryHelper.CreateParentDirectory(Path.GetFullPath(settings.OutputFile));
            await File.WriteAllTextAsync(settings.OutputFile, json, new UTF8Encoding(false), cancellationToken);
            CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已导出至文件: {settings.OutputFile}", settings.Json);
        }

        if (settings.Clipboard)
        {
            var copied = await ClipboardHelper.SetTextAsync(json, cancellationToken);
            if (!copied)
            {
                CliOutputFormatter.PrintError("复制到系统剪贴板失败", settings.Json);
                return 1;
            }
            CliOutputFormatter.PrintSuccess($"任务 '{task.Group}.{task.Name}' 已成功复制到系统剪贴板", settings.Json);
        }

        if (string.IsNullOrWhiteSpace(settings.OutputFile) && !settings.Clipboard)
        {
            Console.Out.WriteLine(json);
        }

        return 0;
    }
}
