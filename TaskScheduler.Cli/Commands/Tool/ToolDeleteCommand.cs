using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Tool;

public sealed class ToolDeleteCommand(IToolConfigService toolService) : AsyncCommand<ToolDeleteCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandArgument(0, "<ID>")]
        [Description("工具配置记录 ID")]
        public int Id { get; set; }

        [CommandOption("-y|--yes")]
        [Description("跳过确认直接删除")]
        public bool ConfirmYes { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var existing = await toolService.GetToolByIdAsync(settings.Id, cancellationToken);
        if (existing == null)
        {
            CliOutputFormatter.PrintError($"未找到 ID 为 {settings.Id} 的工具配置", settings.Json);
            return 1;
        }

        if (!settings.ConfirmYes)
        {
            if (settings.Json || Console.IsInputRedirected)
            {
                CliOutputFormatter.PrintError("此操作不可撤销，如需确认执行请添加 -y/--yes 参数", settings.Json);
                return 1;
            }

            if (!AnsiConsole.Confirm($"确定要删除工具配置 #{existing.Id} \"{existing.VersionName} ({existing.ToolType})\" 吗？", false))
            {
                CliOutputFormatter.PrintInfo("已取消删除操作");
                return 0;
            }
        }

        await toolService.DeleteToolAsync(settings.Id, cancellationToken);
        CliOutputFormatter.PrintSuccess($"工具配置 #{settings.Id} ({existing.VersionName}) 已删除", settings.Json);
        return 0;
    }
}
