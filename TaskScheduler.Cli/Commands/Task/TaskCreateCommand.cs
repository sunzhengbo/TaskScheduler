using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Jobs;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Task;

public sealed class TaskCreateCommand(
    ITaskSchedulerService taskService,
    IToolConfigService toolService) : AsyncCommand<TaskCreateCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-i|--interactive")]
        [Description("启动交互式终端向导逐步创建任务")]
        public bool Interactive { get; set; }

        [CommandOption("-n|--name <NAME>")]
        [Description("任务名称（非交互模式下必填）")]
        public string? Name { get; set; }

        [CommandOption("-g|--group <GROUP>")]
        [Description("任务分组（默认 DEFAULT，可选 SYSTEM, USER, DAEMON）")]
        [DefaultValue("DEFAULT")]
        public string Group { get; set; } = "DEFAULT";

        [CommandOption("-d|--description <DESC>")]
        [Description("任务描述")]
        public string? Description { get; set; }

        [CommandOption("-p|--priority <PRIORITY>")]
        [Description("任务优先级（Low, Normal, High，默认 Normal）")]
        [DefaultValue(TaskPriority.Normal)]
        public TaskPriority Priority { get; set; } = TaskPriority.Normal;

        [CommandOption("-t|--trigger-type <TYPE>")]
        [Description("触发器类型（Cron, Simple, OnStartup，默认 Cron）")]
        [DefaultValue(TriggerType.Cron)]
        public TriggerType TriggerType { get; set; } = TriggerType.Cron;

        [CommandOption("--cron <EXPR>")]
        [Description("Cron 表达式（支持 5 字段或 6-7 字段 Quartz 格式）")]
        public string? CronExpression { get; set; }

        [CommandOption("--interval <MINUTES>")]
        [Description("Simple 触发器重复间隔分钟数（默认 30，需 >= 1）")]
        [DefaultValue(30.0)]
        public double IntervalMinutes { get; set; } = 30.0;

        [CommandOption("--repeat <COUNT>")]
        [Description("Simple 触发器重复次数（默认 -1 表示无限循环）")]
        [DefaultValue(-1)]
        public int RepeatCount { get; set; } = -1;

        [CommandOption("--use-boot-time")]
        [Description("Simple 触发器是否以系统开机时间为锚点计算")]
        public bool UseBootTime { get; set; }

        [CommandOption("--cmd-name <NAME>")]
        [Description("命令名称（默认 命令1）")]
        [DefaultValue("命令1")]
        public string CommandName { get; set; } = "命令1";

        [CommandOption("--cmd-type <TYPE>")]
        [Description("命令类型（PowerShell, Python, Node.js, Bash, Cmd, VBScript，默认 PowerShell）")]
        [DefaultValue("PowerShell")]
        public string CommandType { get; set; } = "PowerShell";

        [CommandOption("--cmd <CONTENT>")]
        [Description("要执行的命令或脚本内容")]
        public string? CommandContent { get; set; }

        [CommandOption("--cmd-file <FILE>")]
        [Description("从指定脚本文件读取命令内容")]
        public string? CommandFile { get; set; }

        [CommandOption("--interpreter <VERSION_OR_PATH>")]
        [Description("指定解释器版本名称或可执行文件路径（不指定则自动选用默认版本）")]
        public string? Interpreter { get; set; }

        [CommandOption("--cmd-desc|--cmd-description <DESC>")]
        [Description("命令自身描述（可选，不指定则为空）")]
        public string? CommandDescription { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var allTools = await toolService.GetAllToolsAsync(cancellationToken);

        if (settings.Interactive)
        {
            PopulateFromInteractiveWizard(settings, allTools);
        }

        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            CliOutputFormatter.PrintError("请输入任务名称（使用 -n/--name 指定，或使用 -i 进入交互式向导）", settings.Json);
            return 2;
        }

        var cmdContent = settings.CommandContent;
        if (string.IsNullOrWhiteSpace(cmdContent) && !string.IsNullOrWhiteSpace(settings.CommandFile))
        {
            if (!File.Exists(settings.CommandFile))
            {
                CliOutputFormatter.PrintError($"指定的脚本文件不存在: {settings.CommandFile}", settings.Json);
                return 2;
            }
            cmdContent = await File.ReadAllTextAsync(settings.CommandFile, cancellationToken);
        }

        if (settings.TriggerType == TriggerType.Cron)
        {
            if (string.IsNullOrWhiteSpace(settings.CronExpression))
            {
                CliOutputFormatter.PrintError("Cron 触发器类型必须提供 --cron <表达式>", settings.Json);
                return 2;
            }

            var normalizedCron = TaskSchedulerService.NormalizeCronExpression(settings.CronExpression);
            if (!Quartz.CronExpression.IsValidExpression(normalizedCron))
            {
                CliOutputFormatter.PrintError($"无效的 Cron 表达式: '{settings.CronExpression}'。支持标准 5 字段（如 '*/5 * * * *'）或 Quartz 6-7 字段格式（如 '0 */5 * * * ?'）。", settings.Json);
                return 2;
            }
        }

        if (settings.TriggerType == TriggerType.Simple && settings.IntervalMinutes < 1)
        {
            CliOutputFormatter.PrintError("Simple 触发器间隔时间（--interval）不能小于 1 分钟", settings.Json);
            return 2;
        }

        if (string.IsNullOrWhiteSpace(cmdContent))
        {
            CliOutputFormatter.PrintError("请输入命令内容（使用 --cmd 或 --cmd-file 指定）", settings.Json);
            return 2;
        }

        var normalizedCmdType = CommandTypes.Normalize(settings.CommandType);
        var interpreterVersion = ResolveInterpreterVersion(normalizedCmdType, settings.Interpreter, allTools);

        var command = new CommandModel
        {
            Name = string.IsNullOrWhiteSpace(settings.CommandName) ? "命令1" : settings.CommandName,
            Type = normalizedCmdType,
            Content = cmdContent,
            InterpreterVersion = interpreterVersion,
            Description = settings.CommandDescription
        };

        var now = DateTime.UtcNow.ToString("O");
        var jobData = new Dictionary<string, object?>
        {
            { "Command", JsonSerializer.Serialize(command) },
            { "Priority", settings.Priority.ToString() },
            { "UseBootTime", settings.UseBootTime.ToString() },
            { "RunOnStartup", (settings.TriggerType == TriggerType.OnStartup).ToString() },
            { "CreatedAt", now },
            { "UpdatedAt", now }
        };

        try
        {
            var request = new TaskCreateRequest
            {
                Name = settings.Name.Trim(),
                Group = string.IsNullOrWhiteSpace(settings.Group) ? "DEFAULT" : settings.Group.Trim(),
                Description = settings.Description,
                JobType = typeof(CommandJob),
                JobData = jobData,
                TriggerType = settings.TriggerType,
                RepeatCount = settings.RepeatCount,
                RepeatInterval = TimeSpan.FromMinutes(settings.IntervalMinutes),
                CronExpression = settings.CronExpression ?? string.Empty,
                UseBootTime = settings.UseBootTime
            };

            var taskId = await taskService.CreateTaskAsync(request, cancellationToken);
            CliOutputFormatter.PrintSuccess($"任务 '{taskId}' 创建成功", settings.Json);
            return 0;
        }
        catch (Exception ex)
        {
            CliOutputFormatter.PrintError($"创建任务失败: {ex.Message}", settings.Json);
            return 1;
        }
    }

    internal static string? ResolveInterpreterVersion(
        string normalizedCmdType,
        string? userInput,
        IReadOnlyList<ToolConfig> allTools)
    {
        var toolType = CommandTypes.MapToToolType(normalizedCmdType);
        if (toolType == null)
        {
            return string.IsNullOrWhiteSpace(userInput) ? null : userInput;
        }

        var matchingTools = allTools.Where(t => t.ToolType.Equals(toolType, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(userInput))
        {
            var cleanName = userInput.Trim();
            if (cleanName.EndsWith(')'))
            {
                var parenIndex = cleanName.IndexOf('(');
                if (parenIndex > 0)
                {
                    cleanName = cleanName[..parenIndex].Trim();
                }
            }

            var matched = matchingTools.FirstOrDefault(t =>
                t.VersionName.Equals(cleanName, StringComparison.OrdinalIgnoreCase) ||
                t.ExecutablePath.Equals(userInput.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matched != null)
                return matched.VersionName;

            return userInput;
        }

        var defaultTool = matchingTools.FirstOrDefault(t => t.IsDefault) ?? matchingTools.FirstOrDefault();
        return defaultTool?.VersionName;
    }

    private static void PopulateFromInteractiveWizard(Settings settings, IReadOnlyList<ToolConfig> allTools)
    {
        AnsiConsole.MarkupLine("[bold cyan]=== 新建定时任务向导 ===[/]");

        settings.Name = AnsiConsole.Ask("任务名称:", settings.Name ?? "新任务");
        settings.Group = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择任务分组:")
                .AddChoices("DEFAULT", "SYSTEM", "USER", "DAEMON"));

        settings.Description = AnsiConsole.Prompt(
            new TextPrompt<string?>("任务描述 (可选，直接回车跳过):")
                .AllowEmpty());

        settings.Priority = AnsiConsole.Prompt(
            new SelectionPrompt<TaskPriority>()
                .Title("选择任务优先级:")
                .AddChoices(TaskPriority.Normal, TaskPriority.High, TaskPriority.Low));

        settings.TriggerType = AnsiConsole.Prompt(
            new SelectionPrompt<TriggerType>()
                .Title("选择触发器类型:")
                .AddChoices(TriggerType.Cron, TriggerType.Simple, TriggerType.OnStartup));

        if (settings.TriggerType == TriggerType.Cron)
        {
            settings.CronExpression = AnsiConsole.Ask("输入 Cron 表达式 (如 '0 */5 * * * ?' 或 '*/5 * * * *'):", settings.CronExpression ?? "0 */30 * * * ?");
        }
        else if (settings.TriggerType == TriggerType.Simple)
        {
            settings.IntervalMinutes = AnsiConsole.Ask("执行间隔 (分钟, >=1):", settings.IntervalMinutes);
            settings.RepeatCount = AnsiConsole.Ask("重复次数 (-1 表示无限重复):", settings.RepeatCount);
            settings.UseBootTime = AnsiConsole.Confirm("是否以系统开机时间为锚点计算触发时刻?", settings.UseBootTime);
        }

        var cmdTypes = CommandTypes.GetConfiguredOrAvailableCommandTypes(allTools);
        settings.CommandType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择命令执行类型:")
                .AddChoices(cmdTypes));

        var toolType = CommandTypes.MapToToolType(settings.CommandType);
        if (toolType != null)
        {
            var versions = allTools
                .Where(t => t.ToolType.Equals(toolType, StringComparison.OrdinalIgnoreCase))
                .Select(t => $"{t.VersionName} ({t.ExecutablePath})")
                .ToList();
            if (versions.Count > 0)
            {
                settings.Interpreter = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("选择解释器版本:")
                        .AddChoices(versions));
            }
        }

        settings.CommandContent = AnsiConsole.Ask<string>("输入要执行的命令内容:");
    }
}
