using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskUpdateCommand(
    ITaskSchedulerService taskService,
    IToolConfigService toolService) : AsyncCommand<TaskUpdateCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("要更新的任务名称")]
        public string Name { get; set; } = string.Empty;

        [CommandOption("-g|--group <GROUP>")]
        [Description("任务所属分组（默认 DEFAULT）")]
        [DefaultValue("DEFAULT")]
        public string Group { get; set; } = "DEFAULT";

        [CommandOption("-d|--description <DESC>")]
        [Description("更新任务描述")]
        public string? Description { get; set; }

        [CommandOption("-p|--priority <PRIORITY>")]
        [Description("更新任务优先级（Low, Normal, High）")]
        public TaskPriority? Priority { get; set; }

        [CommandOption("-t|--trigger-type <TYPE>")]
        [Description("更新触发器类型（Cron, Simple, OnStartup）")]
        public TriggerType? TriggerType { get; set; }

        [CommandOption("--cron <EXPR>")]
        [Description("更新 Cron 表达式")]
        public string? CronExpression { get; set; }

        [CommandOption("--interval <MINUTES>")]
        [Description("更新 Simple 触发器间隔分钟数（>= 1）")]
        public double? IntervalMinutes { get; set; }

        [CommandOption("--repeat <COUNT>")]
        [Description("更新 Simple 触发器重复次数（-1 为无限）")]
        public int? RepeatCount { get; set; }

        [CommandOption("--use-boot-time <BOOL>")]
        [Description("更新 Simple 触发器是否以系统开机时间为锚点")]
        public bool? UseBootTime { get; set; }

        [CommandOption("--cmd-name <NAME>")]
        [Description("更新命令名称")]
        public string? CommandName { get; set; }

        [CommandOption("--cmd-type <TYPE>")]
        [Description("更新命令类型（PowerShell, Python, Node.js, Bash, Cmd, VBScript, C#）")]
        public string? CommandType { get; set; }

        [CommandOption("--cmd <CONTENT>")]
        [Description("更新命令脚本内容")]
        public string? CommandContent { get; set; }

        [CommandOption("--cmd-file <FILE>")]
        [Description("从脚本文件读取并更新命令内容")]
        public string? CommandFile { get; set; }

        [CommandOption("--interpreter <VERSION_OR_PATH>")]
        [Description("更新解释器版本名称或路径")]
        public string? Interpreter { get; set; }

        [CommandOption("--cmd-desc|--cmd-description <DESC>")]
        [Description("更新命令自身描述")]
        public string? CommandDescription { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var existingTask = await taskService.GetTaskAsync(settings.Name, settings.Group, cancellationToken);
        if (existingTask == null)
        {
            CliOutputFormatter.PrintError($"未找到任务 '{settings.Group}.{settings.Name}'", settings.Json);
            return 1;
        }

        var existingCommand = CommandModel.DeserializeFromJson(existingTask.CommandJson) ?? new CommandModel { Name = "命令1" };
        var existingTrigger = existingTask.Triggers.FirstOrDefault();

        var cmdContent = existingCommand.Content;
        if (!string.IsNullOrWhiteSpace(settings.CommandContent))
        {
            cmdContent = settings.CommandContent;
        }
        else if (!string.IsNullOrWhiteSpace(settings.CommandFile))
        {
            if (!File.Exists(settings.CommandFile))
            {
                CliOutputFormatter.PrintError($"脚本文件不存在: {settings.CommandFile}", settings.Json);
                return 2;
            }
            cmdContent = await File.ReadAllTextAsync(settings.CommandFile, cancellationToken);
        }

        var cmdType = !string.IsNullOrWhiteSpace(settings.CommandType)
            ? CommandTypes.Normalize(settings.CommandType)
            : existingCommand.Type;

        var interpreter = existingCommand.InterpreterVersion;
        if (settings.Interpreter != null || !string.IsNullOrWhiteSpace(settings.CommandType))
        {
            var allTools = await toolService.GetAllToolsAsync(cancellationToken);
            interpreter = TaskCreateCommand.ResolveInterpreterVersion(cmdType, settings.Interpreter, allTools);
        }

        var updatedCommand = new CommandModel
        {
            Id = existingCommand.Id,
            Name = settings.CommandName ?? existingCommand.Name,
            Type = cmdType,
            Content = cmdContent,
            InterpreterVersion = interpreter,
            Description = settings.CommandDescription ?? existingCommand.Description,
            CreatedAt = existingCommand.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        var targetTriggerType = settings.TriggerType ?? existingTrigger?.Type ?? TriggerType.Cron;
        var targetPriority = settings.Priority ?? existingTask.Priority;
        var targetUseBootTime = settings.UseBootTime ?? existingTask.UseBootTime;
        var targetDesc = settings.Description ?? existingTask.Description;

        var triggerChanged = settings.TriggerType.HasValue ||
                             !string.IsNullOrWhiteSpace(settings.CronExpression) ||
                             settings.IntervalMinutes.HasValue ||
                             settings.RepeatCount.HasValue ||
                             settings.UseBootTime.HasValue;

        string? resolvedCron = null;
        double resolvedIntervalMins = 30;
        int resolvedRepeatCount = -1;

        if (triggerChanged)
        {
            if (targetTriggerType == TriggerType.Cron)
            {
                resolvedCron = settings.CronExpression ?? existingTrigger?.CronExpression ?? existingTrigger?.TriggerDisplay;
                if (string.IsNullOrWhiteSpace(resolvedCron) || resolvedCron == "启动时运行")
                {
                    CliOutputFormatter.PrintError("切换或更新 Cron 触发器时必须指定 --cron <表达式>", settings.Json);
                    return 2;
                }

                var normalizedCron = TaskSchedulerService.NormalizeCronExpression(resolvedCron);
                if (!Quartz.CronExpression.IsValidExpression(normalizedCron))
                {
                    CliOutputFormatter.PrintError($"无效的 Cron 表达式: '{resolvedCron}'。支持标准 5 字段（如 '*/5 * * * *'）或 Quartz 6-7 字段格式（如 '0 */5 * * * ?'）。", settings.Json);
                    return 2;
                }
            }
            else if (targetTriggerType == TriggerType.Simple)
            {
                resolvedIntervalMins = settings.IntervalMinutes ?? existingTrigger?.RepeatInterval?.TotalMinutes ?? 30;
                if (resolvedIntervalMins < 1)
                {
                    CliOutputFormatter.PrintError("Simple 触发器间隔时间不能小于 1 分钟", settings.Json);
                    return 2;
                }
                resolvedRepeatCount = settings.RepeatCount ?? existingTrigger?.RepeatCount ?? -1;
            }
        }

        var now = DateTime.UtcNow.ToString("O");
        var createdAt = existingTask.CreatedAt?.ToUniversalTime().ToString("O") ?? now;

        var jobData = new Dictionary<string, object?>
        {
            { "Command", JsonSerializer.Serialize(updatedCommand) },
            { "Priority", targetPriority.ToString() },
            { "UseBootTime", targetUseBootTime.ToString() },
            { "RunOnStartup", (targetTriggerType == TriggerType.OnStartup).ToString() },
            { "CreatedAt", createdAt },
            { "UpdatedAt", now }
        };

        try
        {
            await taskService.UpdateTaskAsync(existingTask.Name, existingTask.Group, new TaskUpdateRequest
            {
                Description = targetDesc,
                JobData = jobData
            }, cancellationToken);

            if (triggerChanged)
            {
                var triggerName = !string.IsNullOrWhiteSpace(existingTrigger?.Name)
                    ? existingTrigger.Name
                    : $"{existingTask.Name}_trigger";
                var triggerGroup = !string.IsNullOrWhiteSpace(existingTrigger?.Group)
                    ? existingTrigger.Group
                    : existingTask.Group;

                if (targetTriggerType == TriggerType.Cron)
                {
                    await taskService.UpdateCronTriggerAsync(triggerName, triggerGroup, resolvedCron!, cancellationToken);
                }
                else if (targetTriggerType == TriggerType.OnStartup)
                {
                    await taskService.UpdateOnStartupTriggerAsync(triggerName, triggerGroup, cancellationToken);
                }
                else
                {
                    await taskService.UpdateSimpleTriggerAsync(
                        triggerName,
                        triggerGroup,
                        resolvedRepeatCount,
                        TimeSpan.FromMinutes(resolvedIntervalMins),
                        cancellationToken,
                        targetUseBootTime);
                }
            }

            CliOutputFormatter.PrintSuccess($"任务 '{existingTask.Group}.{existingTask.Name}' 更新成功", settings.Json);
            return 0;
        }
        catch (Exception ex)
        {
            CliOutputFormatter.PrintError($"更新任务失败: {ex.Message}", settings.Json);
            return 1;
        }
    }
}
