using System.ComponentModel;

using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigFormatterClearCommand(IScriptFormatterService formatterService)
    : AsyncCommand<ConfigFormatterClearCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--lang <TYPE>")]
        [Description("清除路径覆盖的语言（Python, PowerShell, Bash, Node.js）")]
        public string? Language { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, "必须指定 --lang <type>。");
        }

        var formatterSettings = await formatterService.GetSettingsAsync(cancellationToken);
        var language = CommandTypes.Normalize(settings.Language);
        switch (language)
        {
            case CommandTypes.Python:
                formatterSettings.BlackExecutablePath = null;
                break;
            case CommandTypes.PowerShell:
                formatterSettings.PsscriptAnalyzerModulePath = null;
                break;
            case CommandTypes.Shell:
                formatterSettings.ShfmtExecutablePath = null;
                break;
            case CommandTypes.NodeJs:
                formatterSettings.PrettierPackagePath = null;
                break;
            default:
                return CliOutputFormatter.WriteCommandError(settings.Json, $"{language} 没有可清除的 formatter 路径。");
        }

        await formatterService.SaveSettingsAsync(formatterSettings, cancellationToken);
        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new { success = true, settings = formatterSettings });
        }
        else
        {
            CliOutputFormatter.PrintSuccess("formatter 路径覆盖已清除。");
        }

        return 0;
    }
}
