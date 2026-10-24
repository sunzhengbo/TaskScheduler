using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Tool;

public sealed class ToolListCommand(IToolConfigService toolService) : AsyncCommand<ToolListCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("-t|--type <TYPE>")]
        [Description("按工具类型过滤（Python, PowerShell, Node.js, Bash, DotNet）")]
        public string? ToolType { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var tools = !string.IsNullOrWhiteSpace(settings.ToolType)
            ? await toolService.GetToolsByTypeAsync(NormalizeToolType(settings.ToolType), cancellationToken)
            : await toolService.GetAllToolsAsync(cancellationToken);

        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(tools);
            return 0;
        }

        var table = new Table()
            .Title($"[bold cyan]解释器工具配置列表 (共 {tools.Count} 项)[/]")
            .Border(TableBorder.Rounded)
            .AddColumn("ID")
            .AddColumn("工具类型")
            .AddColumn("版本名称")
            .AddColumn("可执行文件路径")
            .AddColumn("默认版本")
            .AddColumn("环境变量")
            .AddColumn("添加时间");

        if (tools.Count == 0)
        {
            table.AddRow("—", "[grey]未配置解释器工具[/]", "—", "—", "—", "—", "—");
        }
        else
        {
            foreach (var tool in tools)
            {
                table.AddRow(
                    tool.Id.ToString(),
                    $"[bold]{Markup.Escape(tool.ToolType)}[/]",
                    Markup.Escape(tool.VersionName),
                    Markup.Escape(tool.ExecutablePath),
                    tool.IsDefault ? "[green]★ 默认[/]" : "—",
                    Markup.Escape(tool.EnvVariables ?? "—"),
                    tool.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            }
        }

        AnsiConsole.Write(table);
        return 0;
    }

    internal static string NormalizeToolType(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Trim().ToLowerInvariant() switch
        {
            "python" or "py" => "Python",
            "powershell" or "pwsh" or "ps" => "PowerShell",
            "node.js" or "nodejs" or "node" or "js" => "Node.js",
            "bash" or "sh" or "shell" => "Bash",
            "dotnet" or "c#" or "cs" or "csharp" or ".net" => "DotNet",
            _ => input.Trim()
        };
    }
}
