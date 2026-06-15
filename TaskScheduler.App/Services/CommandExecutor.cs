using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using TaskScheduler.App.Models;

namespace TaskScheduler.App.Services;

public interface ICommandExecutor
{
    Task<CommandResult> ExecuteCommandAsync(string command, string type, string? interpreterPath = null, CancellationToken ct = default);
}

public record CommandResult(int ExitCode, string Output, string Error);

public class CommandExecutor : ICommandExecutor
{
    private readonly ILogger<CommandExecutor> _logger;

    public CommandExecutor(ILogger<CommandExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> ExecuteCommandAsync(string command, string type, string? interpreterPath = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        _logger.LogInformation("Executing command (Type: {Type}, InterpreterPath: {InterpreterPath})", type, interpreterPath);

        var processStartInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        switch (type)
        {
            case CommandTypes.PowerShell:
                processStartInfo.FileName = "pwsh.exe";
                processStartInfo.ArgumentList.Add("-NoProfile");
                processStartInfo.ArgumentList.Add("-ExecutionPolicy");
                processStartInfo.ArgumentList.Add("Bypass");
                processStartInfo.ArgumentList.Add("-Command");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Python:
                processStartInfo.FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "python";
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Shell:
                processStartInfo.FileName = "bash";
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.NodeJs:
                processStartInfo.FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "node";
                processStartInfo.ArgumentList.Add("-e");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.VBScript:
                // VBScript 需要通过临时文件执行，cscript 不支持内联代码
                if (!OperatingSystem.IsWindows())
                    return new CommandResult(-1, string.Empty, "VBScript is only supported on Windows.");
                return await ExecuteVBScriptAsync(command, interpreterPath, ct);

            case CommandTypes.Cmd:
                if (!OperatingSystem.IsWindows())
                    return new CommandResult(-1, string.Empty, "Cmd is only supported on Windows.");
                processStartInfo.FileName = "cmd.exe";
                processStartInfo.ArgumentList.Add("/C");
                processStartInfo.ArgumentList.Add(command);
                break;

            default:
                return new CommandResult(-1, string.Empty, $"Unsupported command type: {type}");
        }

        using var process = new Process();
        process.StartInfo = processStartInfo;

        process.Start();

        Task<string> outputTask;
        Task<string> errorTask;
        try
        {
            outputTask = process.StandardOutput.ReadToEndAsync(ct);
            errorTask = process.StandardError.ReadToEndAsync(ct);

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var outputText = await outputTask;
        var errorText = await errorTask;

        _logger.LogInformation("Command executed with exit code: {ExitCode}", process.ExitCode);

        return new CommandResult(process.ExitCode, outputText ?? string.Empty, errorText ?? string.Empty);
    }

    /// <summary>
    /// 执行 VBScript 脚本。cscript.exe 不支持内联代码，需写入临时文件后执行。
    /// </summary>
    private async Task<CommandResult> ExecuteVBScriptAsync(string command, string? interpreterPath, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"taskscheduler_{Guid.NewGuid():N}.vbs");
        try
        {
            await File.WriteAllTextAsync(tempFile, command, ct);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "cscript.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            processStartInfo.ArgumentList.Add("//NoLogo");
            processStartInfo.ArgumentList.Add(tempFile);

            using var process = new Process();
            process.StartInfo = processStartInfo;
            if (!process.Start())
                return new CommandResult(-1, string.Empty, "Failed to start VBScript process.");

            Task<string> outputTask;
            Task<string> errorTask;
            try
            {
                outputTask = process.StandardOutput.ReadToEndAsync(ct);
                errorTask = process.StandardError.ReadToEndAsync(ct);
                await Task.WhenAll(outputTask, errorTask);
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            var outputText = await outputTask;
            var errorText = await errorTask;

            _logger.LogInformation("VBScript executed with exit code: {ExitCode}", process.ExitCode);
            return new CommandResult(process.ExitCode, outputText ?? string.Empty, errorText ?? string.Empty);
        }
        finally
        {
            try { File.Delete(tempFile); } catch (Exception ex) { _logger.LogWarning(ex, "清理 VBScript 临时文件失败: {Path}", tempFile); }
        }
    }
}
