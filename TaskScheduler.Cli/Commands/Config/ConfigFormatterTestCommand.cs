using System.ComponentModel;

using Spectre.Console;
using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Config;

public sealed class ConfigFormatterTestCommand(IScriptFormatterService formatterService)
    : AsyncCommand<ConfigFormatterTestCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--lang <TYPE>")]
        [Description("要检测的脚本语言")]
        public string? Language { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, "必须指定 --lang <type>。");
        }

        var capability = await formatterService.GetCapabilityAsync(settings.Language, ct: cancellationToken);
        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(capability);
        }
        else if (capability.Available)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(capability.Language)}: {Markup.Escape(capability.Tool ?? "formatter")} {Markup.Escape(capability.Version ?? string.Empty)} 可用[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(capability.Language)}: {Markup.Escape(capability.Reason ?? "不可用")}[/]");
        }

        return capability.Available ? 0 : 1;
    }
}
