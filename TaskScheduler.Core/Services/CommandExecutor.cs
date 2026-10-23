using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public class CommandExecutor(
    ILogger<CommandExecutor> logger,
    IToolConfigService? toolConfigService = null) : ICommandExecutor
{
    public async Task<CommandResult> ExecuteCommandAsync(
        string command,
        string type,
        string? interpreterPath = null,
        CancellationToken ct = default,
        string? envVariables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var normalizedType = CommandTypes.Normalize(type);
        logger.LogInformation("Executing command (Type: {Type}, InterpreterPath: {InterpreterPath})", normalizedType, interpreterPath);

        var resolvedEnvVars = envVariables;
        if (string.IsNullOrWhiteSpace(resolvedEnvVars) && toolConfigService != null)
        {
            var toolType = CommandTypes.MapToToolType(normalizedType);
            if (toolType != null)
            {
                var tools = await toolConfigService.GetToolsByTypeAsync(toolType, ct).ConfigureAwait(false);
                var matchedTool = !string.IsNullOrWhiteSpace(interpreterPath)
                    ? tools.FirstOrDefault(t => t.ExecutablePath.Equals(interpreterPath, StringComparison.OrdinalIgnoreCase))
                    : tools.FirstOrDefault(t => t.IsDefault) ?? tools.FirstOrDefault();
                resolvedEnvVars = matchedTool?.EnvVariables;
            }
        }

        var processStartInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        ApplyEnvironmentVariables(processStartInfo, resolvedEnvVars);

        switch (normalizedType)
        {
            case CommandTypes.PowerShell:
                processStartInfo.FileName = !string.IsNullOrWhiteSpace(interpreterPath)
                    ? interpreterPath
                    : ResolveDefaultPowerShellExecutable();
                processStartInfo.StandardOutputEncoding = Encoding.UTF8;
                processStartInfo.StandardErrorEncoding = Encoding.UTF8;
                processStartInfo.Environment["NO_COLOR"] = "1";
                processStartInfo.ArgumentList.Add("-NoProfile");
                processStartInfo.ArgumentList.Add("-ExecutionPolicy");
                processStartInfo.ArgumentList.Add("Bypass");
                processStartInfo.ArgumentList.Add("-Command");
                processStartInfo.ArgumentList.Add(
                    "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); " +
                    "if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }; " +
                    command);
                break;

            case CommandTypes.Python:
                processStartInfo.FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "python";
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Shell:
                processStartInfo.FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "bash";
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
                return await ExecuteVBScriptAsync(command, interpreterPath, resolvedEnvVars, ct).ConfigureAwait(false);

            case CommandTypes.Cmd:
                if (!OperatingSystem.IsWindows())
                    return new CommandResult(-1, string.Empty, "Cmd is only supported on Windows.");
                processStartInfo.FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "cmd.exe";
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

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        var outputText = await outputTask.ConfigureAwait(false);
        var errorText = await errorTask.ConfigureAwait(false);

        logger.LogInformation("Command executed with exit code: {ExitCode}", process.ExitCode);

        return new CommandResult(process.ExitCode, outputText ?? string.Empty, errorText ?? string.Empty);
    }

    private static void ApplyEnvironmentVariables(ProcessStartInfo psi, string? envVariables)
    {
        if (string.IsNullOrWhiteSpace(envVariables))
            return;

        var entries = envVariables.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var eqIndex = entry.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = entry[..eqIndex].Trim();
            var val = entry[(eqIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
            {
                psi.Environment[key] = val;
            }
        }
    }

    /// <summary>
    /// 执行 VBScript 脚本。cscript.exe 不支持内联代码，需写入临时文件后执行。
    /// </summary>
    private async Task<CommandResult> ExecuteVBScriptAsync(string command, string? interpreterPath, string? envVariables, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"taskscheduler_{Guid.NewGuid():N}.vbs");
        try
        {
            await File.WriteAllTextAsync(tempFile, command, ct).ConfigureAwait(false);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "cscript.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            ApplyEnvironmentVariables(processStartInfo, envVariables);
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
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            var outputText = await outputTask.ConfigureAwait(false);
            var errorText = await errorTask.ConfigureAwait(false);

            logger.LogInformation("VBScript executed with exit code: {ExitCode}", process.ExitCode);
            return new CommandResult(process.ExitCode, outputText ?? string.Empty, errorText ?? string.Empty);
        }
        finally
        {
            try { File.Delete(tempFile); } catch (Exception ex) { logger.LogWarning(ex, "清理 VBScript 临时文件失败: {Path}", tempFile); }
        }
    }

    private static string ResolveDefaultPowerShellExecutable()
    {
        if (IsExecutableInPath("pwsh.exe") || IsExecutableInPath("pwsh"))
            return "pwsh";

        if (OperatingSystem.IsWindows())
            return "powershell.exe";

        return "pwsh";
    }

    private static bool IsExecutableInPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return false;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, exeName)))
                    return true;
            }
            catch
            {
                // 忽略无效 PATH 路径
            }
        }
        return false;
    }
}
