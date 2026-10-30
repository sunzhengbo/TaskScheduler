using System.ComponentModel;

using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigFormatterSetCommand(IScriptFormatterService formatterService)
    : AsyncCommand<ConfigFormatterSetCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--lang <TYPE>")]
        [Description("设置 formatter 路径时指定语言（Python, PowerShell, Bash, Node.js）")]
        public string? Language { get; set; }

        [CommandOption("--path <PATH>")]
        [Description("该语言 formatter 的可执行文件、模块或包路径；配置后覆盖自动发现")]
        public string? Path { get; set; }

        [CommandOption("--timeout-seconds <SECONDS>")]
        [Description(ScriptFormatterDefaults.TimeoutOptionDescription)]
        public int? TimeoutSeconds { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // 空白路径等同于未提供：否则会把 "" 写进设置，show 会显示空白而不是「自动发现」。
        // 但「显式传了 --path 却是空白」与「根本没传 --path」的意图不同，必须分开提示，
        // 否则用户会收到「请指定 --lang/--path」这种与命令行自相矛盾的结论。
        var path = settings.Path?.Trim();
        var hasPath = !string.IsNullOrWhiteSpace(path);

        if (settings.Path is not null && !hasPath)
        {
            return CliOutputFormatter.WriteCommandError(settings.Json,
                "--path 不能为空；如需恢复自动发现，请使用 config formatter clear --lang <TYPE>。");
        }

        if (hasPath && string.IsNullOrWhiteSpace(settings.Language))
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, "设置 formatter 路径时必须同时指定 --lang。");
        }
        if (!hasPath && !settings.TimeoutSeconds.HasValue)
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, "请指定 --lang/--path 或 --timeout-seconds。");
        }

        var formatterSettings = await formatterService.GetSettingsAsync(cancellationToken);
        if (settings.TimeoutSeconds.HasValue)
        {
            formatterSettings.TimeoutSeconds = settings.TimeoutSeconds.Value;
        }

        if (hasPath)
        {
            var language = CommandTypes.Normalize(settings.Language);
            switch (language)
            {
                case CommandTypes.Python:
                    formatterSettings.BlackExecutablePath = path;
                    break;
                case CommandTypes.PowerShell:
                    formatterSettings.PsscriptAnalyzerModulePath = path;
                    break;
                case CommandTypes.Shell:
                    formatterSettings.ShfmtExecutablePath = path;
                    break;
                case CommandTypes.NodeJs:
                    formatterSettings.PrettierPackagePath = path;
                    break;
                default:
                    return CliOutputFormatter.WriteCommandError(settings.Json, $"{language} 不支持配置外部 formatter 路径。");
            }
        }

        try
        {
            await formatterService.SaveSettingsAsync(formatterSettings, cancellationToken);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, ex.Message);
        }

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new { success = true, settings = formatterSettings });
        }
        else
        {
            CliOutputFormatter.PrintSuccess("格式化设置已保存。");
        }

        return 0;
    }
}
