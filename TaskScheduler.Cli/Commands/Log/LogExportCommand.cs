using System.ComponentModel;
using System.Text;
using Spectre.Console.Cli;
using TaskScheduler.Cli.Formatting;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Cli.Commands.Log;

public sealed class LogExportCommand(IExecutionLogService logService) : AsyncCommand<LogExportCommand.Settings>
{
    public sealed class Settings : LogIdSettings
    {
        [CommandOption("-o|--output <FILE>")]
        [Description("导出日志文件路径（默认 <JobName>_<yyyyMMddHHmmss>.log）")]
        public string? OutputFile { get; set; }

        [CommandOption("-c|--clipboard")]
        [Description("将格式化后的日志内容复制到系统剪贴板")]
        public bool Clipboard { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var log = await logService.GetLogByIdAsync(settings.Id, cancellationToken);
        if (log == null)
        {
            CliOutputFormatter.PrintError($"未找到 ID 为 {settings.Id} 的执行日志", settings.Json);
            return 1;
        }

        var text = string.Join(Environment.NewLine, log.FormatTerminalLines());

        if (settings.Clipboard)
        {
            var ok = await ClipboardHelper.SetTextAsync(text, cancellationToken);
            if (!ok)
            {
                CliOutputFormatter.PrintError("复制日志到系统剪贴板失败", settings.Json);
                return 1;
            }
            CliOutputFormatter.PrintSuccess($"日志 #{log.Id} 已成功复制到系统剪贴板", settings.Json);
            if (string.IsNullOrWhiteSpace(settings.OutputFile))
                return 0;
        }

        string targetPath;
        if (string.IsNullOrWhiteSpace(settings.OutputFile))
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeJobName = string.Concat(log.JobName.Select(c => invalidChars.Contains(c) ? '_' : c));
            if (string.IsNullOrWhiteSpace(safeJobName)) safeJobName = "job";
            targetPath = $"{safeJobName}_{log.StartTime:yyyyMMddHHmmss}.log";
        }
        else
        {
            targetPath = settings.OutputFile;
        }

        try
        {
            DirectoryHelper.CreateParentDirectory(Path.GetFullPath(targetPath));
            await File.WriteAllTextAsync(targetPath, text, new UTF8Encoding(false), cancellationToken);
            CliOutputFormatter.PrintSuccess($"日志 #{log.Id} 已导出至文件: {targetPath}", settings.Json);
            return 0;
        }
        catch (Exception ex)
        {
            CliOutputFormatter.PrintError($"导出日志文件失败: {ex.Message}", settings.Json);
            return 1;
        }
    }
}
