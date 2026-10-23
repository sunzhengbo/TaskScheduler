using System.ComponentModel;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigAppearanceSetCommand(ISettingsService settingsService) : AsyncCommand<ConfigAppearanceSetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--theme <MODE>")]
        [Description("设置主题模式（System, Light, Dark）")]
        public string? ThemeMode { get; set; }

        [CommandOption("--compact <BOOL>")]
        [Description("设置是否启用紧凑模式（true / false）")]
        public bool? CompactMode { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var hasChanges = !string.IsNullOrWhiteSpace(settings.ThemeMode) || settings.CompactMode.HasValue;
        var appearanceJson = await settingsService.GetValueAsync("appearance_settings", cancellationToken);
        var appearance = AppearanceSettings.FromJson(appearanceJson);

        if (!hasChanges)
        {
            if (settings.Json)
            {
                CliOutputFormatter.WriteJson(appearance);
                return 0;
            }

            CliOutputFormatter.PrintInfo("未提供任何需修改的外观配置参数。使用 --help 查看支持的选项。");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.ThemeMode))
        {
            var normalized = settings.ThemeMode.Trim().ToLowerInvariant() switch
            {
                "system" => "System",
                "light" => "Light",
                "dark" => "Dark",
                _ => null
            };
            if (normalized == null)
            {
                CliOutputFormatter.PrintError("主题模式仅支持 System, Light 或 Dark", settings.Json);
                return 2;
            }
            appearance.ThemeMode = normalized;
        }

        if (settings.CompactMode.HasValue)
        {
            appearance.CompactMode = settings.CompactMode.Value;
        }

        await settingsService.SetValueAsync("appearance_settings", appearance.ToJson(), cancellationToken);

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(appearance);
            return 0;
        }

        CliOutputFormatter.PrintSuccess("外观设置已保存");
        return 0;
    }
}
