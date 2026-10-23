using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigResetCommand(ISettingsService settingsService) : AsyncCommand<ConfigResetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("跳过确认直接重置所有设置为默认值")]
        public bool ConfirmYes { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.ConfirmYes)
        {
            if (settings.Json || Console.IsInputRedirected)
            {
                CliOutputFormatter.PrintError("此操作不可撤销，如需确认执行请添加 -y/--yes 参数", settings.Json);
                return 1;
            }

            if (!AnsiConsole.Confirm("确定要将引擎设置与外观设置恢复为系统默认值吗？", false))
            {
                CliOutputFormatter.PrintInfo("已取消重置");
                return 0;
            }
        }

        var defaultEngine = new EngineSettings();
        var defaultAppearance = new AppearanceSettings();

        await settingsService.SetValueAsync("engine_settings", defaultEngine.ToJson(), cancellationToken);
        await settingsService.SetValueAsync("appearance_settings", defaultAppearance.ToJson(), cancellationToken);
        StartupHelper.SetStartupOnBoot(defaultEngine.StartupEnabled, defaultEngine.StartupMinimize);

        CliOutputFormatter.PrintSuccess("所有全局设置已重置为默认值", settings.Json);
        return 0;
    }
}
