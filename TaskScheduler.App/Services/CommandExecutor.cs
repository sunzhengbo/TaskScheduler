using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using TaskScheduler.App.Models;

namespace TaskScheduler.App.Services;

public interface ICommandExecutor
{
    Task<CommandResult> ExecuteCommandAsync(string command, string type, CancellationToken ct = default);
}

public record CommandResult(int ExitCode, string Output, string Error);

public class CommandExecutor : ICommandExecutor
{
    private readonly ILogger<CommandExecutor> _logger;

    public CommandExecutor(ILogger<CommandExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> ExecuteCommandAsync(string command, string type, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        _logger.LogInformation("Executing command (Type: {Type})", type);

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
                processStartInfo.FileName = "python";
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Shell:
                processStartInfo.FileName = "bash";
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.NodeJs:
                processStartInfo.FileName = "node";
                processStartInfo.ArgumentList.Add("-e");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Cmd:
            default:
                processStartInfo.FileName = "cmd.exe";
                processStartInfo.ArgumentList.Add("/C");
                processStartInfo.ArgumentList.Add(command);
                break;
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
}
