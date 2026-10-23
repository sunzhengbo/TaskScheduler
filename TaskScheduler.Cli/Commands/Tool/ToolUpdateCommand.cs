using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Tool;

public sealed class ToolUpdateCommand(IToolConfigService toolService) : AsyncCommand<ToolUpdateCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("工具配置记录 ID")]
        public int Id { get; set; }

        [CommandOption("-v|--version <NAME>")]
        [Description("更新版本显示名称")]
        public string? VersionName { get; set; }

        [CommandOption("-p|--path <PATH>")]
        [Description("更新可执行文件完整路径")]
        public string? ExecutablePath { get; set; }

        [CommandOption("-e|--env <VARS>")]
        [Description("更新环境变量配置（传入空字符串清空）")]
        public string? EnvVariables { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var existing = await toolService.GetToolByIdAsync(settings.Id, cancellationToken);
        if (existing == null)
        {
            CliOutputFormatter.PrintError($"未找到 ID 为 {settings.Id} 的工具配置", settings.Json);
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(settings.VersionName))
            existing.VersionName = settings.VersionName.Trim();
        if (!string.IsNullOrWhiteSpace(settings.ExecutablePath))
            existing.ExecutablePath = settings.ExecutablePath.Trim();
        if (settings.EnvVariables != null)
            existing.EnvVariables = string.IsNullOrWhiteSpace(settings.EnvVariables) ? null : settings.EnvVariables.Trim();

        await toolService.UpdateToolAsync(existing, cancellationToken);
        CliOutputFormatter.PrintSuccess($"工具配置 #{existing.Id} ({existing.VersionName}) 已更新", settings.Json);
        return 0;
    }
}
