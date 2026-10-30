using System.ComponentModel;

using Spectre.Console.Cli;

using TaskScheduler.Cli.Formatting;
using TaskScheduler.Cli.Infrastructure;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Cli.Commands.Format;

public sealed class FormatCommand(IScriptFormatterService formatterService) : AsyncCommand<FormatCommand.Settings>
{
    public sealed class Settings : GlobalCommandSettings
    {
        [CommandOption("--lang <TYPE>")]
        [Description("脚本命令类型（C#, Python, PowerShell, Bash, Node.js）")]
        public string? Language { get; set; }

        [CommandOption("--file <PATH>")]
        [Description("读取脚本文件内容，只读，不会覆盖文件；省略时从 stdin 读取")]
        public string? FilePath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, "必须指定 --lang <type>。");
        }

        string source;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.FilePath))
            {
                if (!File.Exists(settings.FilePath))
                {
                    return CliOutputFormatter.WriteCommandError(settings.Json, $"脚本文件不存在：{settings.FilePath}");
                }

                source = await File.ReadAllTextAsync(settings.FilePath, cancellationToken);
            }
            else
            {
                if (!Console.IsInputRedirected)
                {
                    return CliOutputFormatter.WriteCommandError(settings.Json,
                        "请通过 --file 指定文件，或将脚本内容通过 stdin 传入。");
                }

                source = await Console.In.ReadToEndAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CliOutputFormatter.WriteCommandError(settings.Json, $"读取脚本内容失败：{ex.Message}");
        }

        var result = await formatterService.FormatAsync(settings.Language, source, ct: cancellationToken);
        if (settings.Json)
        {
            CliOutputFormatter.WriteJson(result);
        }
        else if (result.Ok)
        {
            Console.Out.Write(result.Output);
        }
        else
        {
            Console.Error.WriteLine(result.Error ?? "格式化失败。");
        }

        return result.Ok ? 0 : 1;
    }
}
