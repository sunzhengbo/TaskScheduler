using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public partial class CommandExecutor(
    ILogger<CommandExecutor> logger,
    IToolConfigService? toolConfigService = null) : ICommandExecutor
{
    public async Task<CommandResult> ExecuteCommandAsync(
        string command,
        string type,
        string? interpreter = null,
        CancellationToken ct = default,
        string? envVariables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var normalizedType = CommandTypes.Normalize(type);

        var (resolvedExecutable, resolvedEnvVars) = await ResolveInterpreterAsync(
            normalizedType,
            interpreter,
            envVariables,
            ct).ConfigureAwait(false);

        logger.LogInformation("Executing command (Type: {Type}, Executable: {Executable})", normalizedType, resolvedExecutable);

        if (normalizedType == CommandTypes.VBScript)
        {
            if (!OperatingSystem.IsWindows())
                return new CommandResult(-1, string.Empty, "VBScript is only supported on Windows.");
            return await ExecuteVBScriptAsync(command, resolvedExecutable, resolvedEnvVars, ct).ConfigureAwait(false);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        ApplyEnvironmentVariables(processStartInfo, resolvedEnvVars);

        switch (normalizedType)
        {
            case CommandTypes.PowerShell:
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
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Shell:
                processStartInfo.ArgumentList.Add("-c");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.NodeJs:
                processStartInfo.ArgumentList.Add("-e");
                processStartInfo.ArgumentList.Add(command);
                break;

            case CommandTypes.Cmd:
                if (!OperatingSystem.IsWindows())
                    return new CommandResult(-1, string.Empty, "Cmd is only supported on Windows.");
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

    /// <summary>
    /// 解析目标解释器的真实可执行文件路径与关联环境变量。
    /// 优先级：版本名称动态匹配 -> 候选有效路径校验 -> 同名文件迁移匹配 -> 默认工具配置 -> 系统 PATH 查找 -> 系统内置默认回退。
    /// </summary>
    private async Task<(string ExecutablePath, string? EnvVariables)> ResolveInterpreterAsync(
        string normalizedType,
        string? interpreterInput,
        string? explicitEnvVars,
        CancellationToken ct)
    {
        string? candidateName = null;
        string? candidatePath = null;

        if (!string.IsNullOrWhiteSpace(interpreterInput))
        {
            var match = InterpreterSnapshotRegex().Match(interpreterInput.Trim());
            if (match.Success)
            {
                candidateName = match.Groups[1].Value.Trim();
                candidatePath = match.Groups[2].Value.Trim();
            }
            else
            {
                if (interpreterInput.Contains('/') || interpreterInput.Contains('\\') ||

                    File.Exists(interpreterInput))
                {
                    candidatePath = interpreterInput.Trim();
                }
                else
                {
                    candidateName = interpreterInput.Trim();
                }
            }
        }

        var resolvedEnvVars = explicitEnvVars;
        var toolType = CommandTypes.MapToToolType(normalizedType);
        IReadOnlyList<ToolConfig>? tools = null;

        if (toolConfigService != null && toolType != null)
        {
            tools = await toolConfigService.GetToolsByTypeAsync(toolType, ct).ConfigureAwait(false);

            // 1. 优先根据解释器名称从工具库匹配最新配置的路径
            if (!string.IsNullOrWhiteSpace(candidateName) && tools.Count > 0)
            {
                var matchedByName = tools.FirstOrDefault(t => t.VersionName.Equals(candidateName, StringComparison.OrdinalIgnoreCase));
                if (matchedByName != null)
                {
                    resolvedEnvVars ??= matchedByName.EnvVariables;
                    if (File.Exists(matchedByName.ExecutablePath))
                    {
                        logger.LogInformation("根据解释器名称 '{VersionName}' 动态解析到有效路径: {Path}", matchedByName.VersionName, matchedByName.ExecutablePath);
                        return (matchedByName.ExecutablePath, resolvedEnvVars);
                    }

                    logger.LogWarning("解释器 '{VersionName}' 在工具库中配置的路径不存在: {Path}，尝试容错查找...", matchedByName.VersionName, matchedByName.ExecutablePath);
                }
            }
        }

        // 2. 检查候选文件路径是否在本地有效存在（无论是通过绝对路径传入还是由名称推导，不受 toolConfigService 限制）
        if (!string.IsNullOrWhiteSpace(candidatePath) && File.Exists(candidatePath))
        {
            if (tools != null)
            {
                var matchedByPath = tools.FirstOrDefault(t => t.ExecutablePath.Equals(candidatePath, StringComparison.OrdinalIgnoreCase));
                resolvedEnvVars ??= matchedByPath?.EnvVariables;
            }
            return (candidatePath, resolvedEnvVars);
        }

        // 3. 原路径失效时，按可执行文件名在工具库中寻找可用的替代工具
        if (!string.IsNullOrWhiteSpace(candidatePath) && tools != null && tools.Count > 0)
        {
            string? exeFileName = null;
            try
            {
                exeFileName = Path.GetFileName(candidatePath);
            }
            catch
            {
                // 忽略包含非法路径字符的输入
            }

            if (!string.IsNullOrWhiteSpace(exeFileName))
            {
                var matchedByFileName = tools.FirstOrDefault(t =>
                    Path.GetFileName(t.ExecutablePath).Equals(exeFileName, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(t.ExecutablePath));
                if (matchedByFileName != null)
                {
                    logger.LogWarning("原解释器路径 '{OldPath}' 不存在，自动迁移至工具库同名解释器 '{VersionName}': {NewPath}",
                        candidatePath, matchedByFileName.VersionName, matchedByFileName.ExecutablePath);
                    resolvedEnvVars ??= matchedByFileName.EnvVariables;
                    return (matchedByFileName.ExecutablePath, resolvedEnvVars);
                }
            }
        }

        // 4. 回退到该语言类型的默认工具配置
        if (tools != null && tools.Count > 0)
        {
            var defaultTool = tools.FirstOrDefault(t => t.IsDefault && File.Exists(t.ExecutablePath)) ??
                              tools.FirstOrDefault(t => File.Exists(t.ExecutablePath));
            if (defaultTool != null)
            {
                logger.LogWarning("未找到指定的有效解释器 '{Input}'，自动回退到默认工具 '{VersionName}': {Path}",
                    interpreterInput, defaultTool.VersionName, defaultTool.ExecutablePath);
                resolvedEnvVars ??= defaultTool.EnvVariables;
                return (defaultTool.ExecutablePath, resolvedEnvVars);
            }
        }

        // 5. 从系统环境变量 PATH 中查找
        var foundInPath = FindInPath(normalizedType, candidateName, candidatePath);
        if (foundInPath != null)
        {
            logger.LogInformation("通过系统环境变量 PATH 找到可用解释器: {Path}", foundInPath);
            return (foundInPath, resolvedEnvVars);
        }

        // 6. 最终内置默认命令名回退
        var fallback = normalizedType switch
        {
            CommandTypes.PowerShell => ResolveDefaultPowerShellExecutable(),
            CommandTypes.Python => "python",
            CommandTypes.NodeJs => "node",
            CommandTypes.Shell => "bash",
            CommandTypes.Cmd => "cmd.exe",
            CommandTypes.VBScript => "cscript.exe",
            _ => !string.IsNullOrWhiteSpace(candidatePath) ? candidatePath : (candidateName ?? string.Empty)
        };

        return (fallback, resolvedEnvVars);
    }

    private static string? FindInPath(string normalizedType, string? candidateName, string? candidatePath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(candidateName))
        {
            candidates.Add(candidateName);
        }
        if (!string.IsNullOrWhiteSpace(candidatePath))
        {
            try
            {
                candidates.Add(Path.GetFileName(candidatePath));
            }
            catch
            {
                // 忽略包含非法路径字符的输入
            }
        }

        switch (normalizedType)
        {
            case CommandTypes.PowerShell:
                candidates.Add("pwsh.exe");
                candidates.Add("pwsh");
                candidates.Add("powershell.exe");
                break;
            case CommandTypes.Python:
                candidates.Add("python.exe");
                candidates.Add("python3.exe");
                candidates.Add("python");
                break;
            case CommandTypes.NodeJs:
                candidates.Add("node.exe");
                candidates.Add("node");
                break;
            case CommandTypes.Shell:
                candidates.Add("bash.exe");
                candidates.Add("bash");
                candidates.Add("sh.exe");
                candidates.Add("sh");
                break;
            case CommandTypes.Cmd:
                candidates.Add("cmd.exe");
                break;
            case CommandTypes.VBScript:
                candidates.Add("cscript.exe");
                break;
        }

        foreach (var name in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = SearchPathEnvironment(name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string? SearchPathEnvironment(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(fileName)
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(dir, fileName + ext);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
            catch
            {
                // 忽略非法的目录格式
            }
        }

        return null;
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
    private async Task<CommandResult> ExecuteVBScriptAsync(string command, string interpreterPath, string? envVariables, CancellationToken ct)
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
        if (SearchPathEnvironment("pwsh") != null)
            return "pwsh";

        if (OperatingSystem.IsWindows())
            return "powershell.exe";

        return "pwsh";
    }

    [GeneratedRegex(@"^(.+?)\s*\((.+)\)$")]
    private static partial Regex InterpreterSnapshotRegex();
}
