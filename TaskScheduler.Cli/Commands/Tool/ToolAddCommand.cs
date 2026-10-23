using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Tool;

public sealed class ToolAddCommand(IToolConfigService toolService) : AsyncCommand<ToolAddCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-t|--type <TYPE>")]
        [Description("工具类型（必填：Python, PowerShell, Node.js, Bash）")]
        public string ToolType { get; set; } = string.Empty;

        [CommandOption("-v|--version <NAME>")]
        [Description("版本显示名称（必填，如 'Python 3.12'）")]
        public string VersionName { get; set; } = string.Empty;

        [CommandOption("-p|--path <PATH>")]
        [Description("可执行文件完整路径（必填）")]
        public string ExecutablePath { get; set; } = string.Empty;

        [CommandOption("-e|--env <VARS>")]
        [Description("环境变量配置（可选，如 KEY1=VAL1;KEY2=VAL2）")]
        public string? EnvVariables { get; set; }

        [CommandOption("--default")]
        [Description("添加后立即设为该工具类型的默认版本")]
        public bool SetDefault { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ToolType) ||
            string.IsNullOrWhiteSpace(settings.VersionName) ||
            string.IsNullOrWhiteSpace(settings.ExecutablePath))
        {
            CliOutputFormatter.PrintError("请提供完整的工具类型 (-t)、版本名称 (-v) 与可执行文件路径 (-p)", settings.Json);
            return 2;
        }

        var normalizedType = ToolListCommand.NormalizeToolType(settings.ToolType);
        var tool = new ToolConfig
        {
            ToolType = normalizedType,
            VersionName = settings.VersionName.Trim(),
            ExecutablePath = settings.ExecutablePath.Trim(),
            EnvVariables = string.IsNullOrWhiteSpace(settings.EnvVariables) ? null : settings.EnvVariables.Trim()
        };

        var existingTools = await toolService.GetAllToolsAsync(cancellationToken);
        if (existingTools.Any(t => t.ToolType.Equals(normalizedType, StringComparison.OrdinalIgnoreCase) &&
                                   t.VersionName?.Trim().Equals(tool.VersionName, StringComparison.OrdinalIgnoreCase) == true))
        {
            CliOutputFormatter.PrintError($"【{normalizedType}】下已存在同名工具版本 '{tool.VersionName}'，名称不可重复", settings.Json);
            return 2;
        }

        var anySameName = existingTools.FirstOrDefault(t => t.VersionName?.Trim().Equals(tool.VersionName, StringComparison.OrdinalIgnoreCase) == true);
        if (anySameName != null)
        {
            CliOutputFormatter.PrintError($"已存在同名工具版本 '{tool.VersionName}'（所属类型: {anySameName.ToolType}），名称不可重复", settings.Json);
            return 2;
        }

        var added = await toolService.AddToolAsync(tool, cancellationToken);
        if (settings.SetDefault)
        {
            await toolService.SetDefaultVersionAsync(added.Id, cancellationToken);
            added.IsDefault = true;
        }

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(added);
            return 0;
        }

        CliOutputFormatter.PrintSuccess($"工具版本 '{added.VersionName}' (ID: {added.Id}, 类型: {added.ToolType}) 已添加");
        return 0;
    }
}
