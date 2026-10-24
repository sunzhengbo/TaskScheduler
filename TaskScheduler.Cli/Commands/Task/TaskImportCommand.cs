using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Jobs;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskImportCommand(ITaskSchedulerService taskService) : AsyncCommand<TaskImportCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "[SOURCE]")]
        [Description("JSON 文件路径或内联的任务 JSON 字符串")]
        public string? Source { get; set; }

        [CommandOption("-c|--clipboard")]
        [Description("从系统剪贴板读取任务 JSON 并导入")]
        public bool FromClipboard { get; set; }

        [CommandOption("--overwrite")]
        [Description("若同名同分组任务已存在，是否覆盖更新")]
        public bool Overwrite { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        string? json = null;

        if (settings.FromClipboard)
        {
            json = await ClipboardHelper.GetTextAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                CliOutputFormatter.PrintError("系统剪贴板为空或无法读取文本", settings.Json);
                return 1;
            }
        }
        else if (settings.Source == "-")
        {
            json = await Console.In.ReadToEndAsync(cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(settings.Source))
        {
            if (File.Exists(settings.Source))
            {
                json = await File.ReadAllTextAsync(settings.Source, new UTF8Encoding(false), cancellationToken);
            }
            else if (settings.Source.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                     settings.Source.Contains('/') || settings.Source.Contains('\\') ||

                     settings.Source.StartsWith('.'))
            {
                CliOutputFormatter.PrintError($"指定的导入文件不存在: {settings.Source}", settings.Json);
                return 2;
            }
            else
            {
                json = settings.Source;
            }
        }
        else if (Console.IsInputRedirected)
        {
            json = await Console.In.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            CliOutputFormatter.PrintError("请提供要导入的 JSON 文件路径、JSON 字符串，或使用 --clipboard 从剪贴板导入", settings.Json);
            return 2;
        }

        var model = TaskExportModel.FromJson(json);
        if (model == null || string.IsNullOrWhiteSpace(model.Name))
        {
            CliOutputFormatter.PrintError("导入失败：无效的任务 JSON 格式", settings.Json);
            return 2;
        }

        if (model.Command == null || string.IsNullOrWhiteSpace(model.Command.Content))
        {
            CliOutputFormatter.PrintError("导入失败：任务命令脚本内容 (Command.Content) 不能为空", settings.Json);
            return 2;
        }

        if (model.TriggerType == TriggerType.Cron && string.IsNullOrWhiteSpace(model.CronExpression))
        {
            CliOutputFormatter.PrintError("导入失败：Cron 触发器任务缺少 CronExpression", settings.Json);
            return 2;
        }

        var repeatInterval = TimeSpan.TryParse(model.RepeatInterval, out var parsedInterval)
            ? parsedInterval
            : TimeSpan.FromMinutes(30);

        if (model.TriggerType == TriggerType.Simple && repeatInterval.TotalMinutes < 1)
        {
            CliOutputFormatter.PrintError("导入失败：Simple 触发器重复间隔不能小于 1 分钟", settings.Json);
            return 2;
        }

        var group = string.IsNullOrWhiteSpace(model.Group) ? "DEFAULT" : model.Group;
        var existing = await taskService.GetTaskAsync(model.Name, group, cancellationToken);
        if (existing != null && !settings.Overwrite)
        {
            CliOutputFormatter.PrintError($"任务 '{group}.{model.Name}' 已存在（如需覆盖请添加 --overwrite 参数）", settings.Json);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(model.Command.ScriptMode) && !ScriptModes.IsValid(model.Command.ScriptMode))
        {
            CliOutputFormatter.PrintError($"导入失败: 任务 '{model.Name}' 包含无效的执行模式 '{model.Command.ScriptMode}'，仅支持 '{ScriptModes.Inline}' 或 '{ScriptModes.File}'", settings.Json);
            return 2;
        }

        string resolvedImportMode;
        if (!string.IsNullOrWhiteSpace(model.Command.ScriptMode))
        {
            resolvedImportMode = ScriptModes.Normalize(model.Command.ScriptMode);
        }
        else
        {
            // 旧版未指定 scriptMode 导出文件：按内容与类型统一兼容推断
            resolvedImportMode = ScriptModes.InferLegacyMode(model.Command.Type, model.Command.Content);
        }

        var command = new CommandModel
        {
            Name = string.IsNullOrWhiteSpace(model.Command.Name) ? "命令1" : model.Command.Name,
            Type = CommandTypes.Normalize(model.Command.Type),
            Content = model.Command.Content ?? string.Empty,
            ScriptMode = resolvedImportMode,
            InterpreterVersion = model.Command.InterpreterVersion,
            Description = model.Command.Description
        };

        var now = DateTime.UtcNow.ToString("O");
        var jobData = new Dictionary<string, object?>
        {
            { "Command", JsonSerializer.Serialize(command) },
            { "Priority", model.Priority.ToString() },
            { "UseBootTime", model.UseBootTime.ToString() },
            { "RunOnStartup", (model.TriggerType == TriggerType.OnStartup).ToString() },
            { "CreatedAt", existing?.CreatedAt?.ToUniversalTime().ToString("O") ?? now },
            { "UpdatedAt", now }
        };

        try
        {
            if (existing != null && settings.Overwrite)
            {
                await taskService.UpdateTaskAsync(model.Name, group, new TaskUpdateRequest
                {
                    Description = model.Description,
                    JobData = jobData
                }, cancellationToken);

                var existingTrigger = existing.Triggers.FirstOrDefault();
                var triggerName = !string.IsNullOrWhiteSpace(existingTrigger?.Name)
                    ? existingTrigger.Name
                    : $"{model.Name}_trigger";
                var triggerGroup = !string.IsNullOrWhiteSpace(existingTrigger?.Group)
                    ? existingTrigger.Group
                    : group;

                if (model.TriggerType == TriggerType.Cron)
                {
                    await taskService.UpdateCronTriggerAsync(triggerName, triggerGroup, model.CronExpression, cancellationToken);
                }
                else if (model.TriggerType == TriggerType.OnStartup)
                {
                    await taskService.UpdateOnStartupTriggerAsync(triggerName, triggerGroup, cancellationToken);
                }
                else
                {
                    await taskService.UpdateSimpleTriggerAsync(
                        triggerName,
                        triggerGroup,
                        model.RepeatCount,
                        repeatInterval,
                        cancellationToken,
                        model.UseBootTime);
                }

                CliOutputFormatter.PrintSuccess($"任务 '{group}.{model.Name}' 覆盖导入成功", settings.Json);
                return 0;
            }

            var createReq = new TaskCreateRequest
            {
                Name = model.Name,
                Group = group,
                Description = model.Description,
                JobType = typeof(CommandJob),
                JobData = jobData,
                TriggerType = model.TriggerType,
                RepeatCount = model.RepeatCount,
                RepeatInterval = repeatInterval,
                CronExpression = model.CronExpression,
                UseBootTime = model.UseBootTime
            };

            var taskId = await taskService.CreateTaskAsync(createReq, cancellationToken);
            CliOutputFormatter.PrintSuccess($"任务 '{taskId}' 导入成功", settings.Json);
            return 0;
        }
        catch (Exception ex)
        {
            CliOutputFormatter.PrintError($"导入任务失败: {ex.Message}", settings.Json);
            return 1;
        }
    }
}
