using Spectre.Console;
using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigFormatterShowCommand(IScriptFormatterService formatterService)
    : AsyncCommand<ConfigFormatterShowCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var formatterSettings = await formatterService.GetSettingsAsync(cancellationToken);
        var capabilities = await GetCapabilitiesAsync(cancellationToken);
        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(new { settings = formatterSettings, capabilities });
            return 0;
        }

        var table = new Table()
            .Title("[bold cyan]脚本格式化设置[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("语言")
            .AddColumn("工具")
            .AddColumn("版本")
            .AddColumn("状态");

        foreach (var capability in capabilities)
        {
            table.AddRow(
                Markup.Escape(capability.Language),
                Markup.Escape(capability.Tool ?? "—"),
                Markup.Escape(capability.Version ?? "—"),
                capability.Available ? "[green]可用[/]" : $"[yellow]{Markup.Escape(capability.Reason ?? "不可用")}[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"超时：{formatterSettings.TimeoutSeconds} 秒");
        AnsiConsole.MarkupLine($"Black 路径：{Markup.Escape(DescribePath(formatterSettings.BlackExecutablePath))}");
        AnsiConsole.MarkupLine($"PSScriptAnalyzer 模块：{Markup.Escape(DescribePath(formatterSettings.PsscriptAnalyzerModulePath))}");
        AnsiConsole.MarkupLine($"shfmt 路径：{Markup.Escape(DescribePath(formatterSettings.ShfmtExecutablePath))}");
        AnsiConsole.MarkupLine($"Prettier 路径：{Markup.Escape(DescribePath(formatterSettings.PrettierPackagePath))}");
        return 0;
    }

    /// <summary>
    /// 路径为空或全空白时按「自动发现」显示。只判断 null 会在历史数据里留下冒号后一片空白，
    /// 让用户误以为配置被清空或读取失败。
    /// </summary>
    private static string DescribePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "自动发现" : path.Trim();

    private async Task<ScriptFormatterCapability[]> GetCapabilitiesAsync(CancellationToken ct) =>
        await System.Threading.Tasks.Task.WhenAll(
            CommandTypes.FormattableTypes.Select(language => formatterService.GetCapabilityAsync(language, ct: ct)));
}
