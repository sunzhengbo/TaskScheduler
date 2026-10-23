using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Tool;

public sealed class ToolSetDefaultCommand(IToolConfigService toolService) : AsyncCommand<ToolSetDefaultCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("工具配置记录 ID")]
        public int Id { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var existing = await toolService.GetToolByIdAsync(settings.Id, cancellationToken);
        if (existing == null)
        {
            CliOutputFormatter.PrintError($"未找到 ID 为 {settings.Id} 的工具配置", settings.Json);
            return 1;
        }

        await toolService.SetDefaultVersionAsync(settings.Id, cancellationToken);
        CliOutputFormatter.PrintSuccess($"已将 #{existing.Id} '{existing.VersionName}' 设为 {existing.ToolType} 的默认版本", settings.Json);
        return 0;
    }
}
