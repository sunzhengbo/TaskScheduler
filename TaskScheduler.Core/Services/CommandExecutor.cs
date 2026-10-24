using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

public partial class CommandExecutor(
    ILogger<CommandExecutor> logger,
    IToolConfigService? toolConfigService = null,
    IInterpreterDetectionService? detectionService = null) : ICommandExecutor
{
    private readonly IInterpreterDetectionService _detectionService = detectionService ?? InterpreterDetectionService.Default;

    public async Task<CommandResult> ExecuteCommandAsync(
        string command,
        string type,
        string? interpreter = null,
        CancellationToken ct = default,
        string? envVariables = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var normalizedType = CommandTypes.Normalize(type);

        // 对特定命令类型的参数进行快速预检（无需解释器参与，节省探测开销并提供精准错误反馈）
        if (normalizedType == CommandTypes.CSharp)
        {
            var precheckResult = PrevalidateCSharpCommand(command);
            if (precheckResult != null)
                return precheckResult;
        }

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

        if (normalizedType == CommandTypes.CSharp)
        {
            return await ExecuteCSharpAsync(command, resolvedExecutable, resolvedEnvVars, ct).ConfigureAwait(false);
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
    /// 严格遵循三级正交模型：
    /// 0. 用户显式传入真实物理绝对路径（直接采纳，最高优先级，反查工具库合并环境变量）
    /// 1. 工具表查询（版本名精准匹配 -> 脱落自愈回退默认工具 -> 快照残留路径兜底）
    /// 2. 系统 PATH 两段式查找（用户候选名 -> 内置候选名 -> Windows PowerShell 特例）
    /// 3. 版本管理器探测器轮询（mise -> scoop -> uv -> asdf -> nvm -> fnm -> volta，带 5 分钟 TTL 缓存）
    /// 终点: 明确抛出带有三级诊断排查摘要的 FileNotFoundException
    /// </summary>
    private async Task<(string ExecutablePath, string? EnvVariables)> ResolveInterpreterAsync(
        string normalizedType,
        string? interpreterInput,
        string? explicitEnvVars,
        CancellationToken ct)
    {
        var resolvedEnvVars = explicitEnvVars;
        var toolType = CommandTypes.MapToToolType(normalizedType);

        // =========================================================================
        // 0. 输入语义统一提取
        // =========================================================================
        string? snapshotName = null;
        string? snapshotPath = null;
        bool isExplicitRawPath = false;

        if (!string.IsNullOrWhiteSpace(interpreterInput))
        {
            var trimmed = interpreterInput.Trim();
            var match = InterpreterSnapshotRegex().Match(trimmed);
            if (match.Success)
            {
                snapshotName = match.Groups[1].Value.Trim();
                snapshotPath = match.Groups[2].Value.Trim();
            }
            else if (Path.IsPathRooted(trimmed) || trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                snapshotPath = trimmed;
                if (File.Exists(trimmed))
                {
                    isExplicitRawPath = true;
                }
            }
            else
            {
                snapshotName = trimmed;
            }
        }

        // 0.1 用户显式指定的现有裸物理路径：采纳并合并工具库环境变量（响应 Review v3 备注 C）
        if (isExplicitRawPath && !string.IsNullOrWhiteSpace(snapshotPath))
        {
            if (toolConfigService != null && toolType != null)
            {
                var rawTools = await toolConfigService.GetToolsByTypeAsync(toolType, ct).ConfigureAwait(false);
                var matchedByPath = rawTools.FirstOrDefault(t => t.ExecutablePath.Equals(snapshotPath, StringComparison.OrdinalIgnoreCase));
                resolvedEnvVars ??= matchedByPath?.EnvVariables;
            }
            return (snapshotPath, resolvedEnvVars);
        }

        // =========================================================================
        // 第 1 级: 用户工具表 (ToolConfig) - 最高托管权威
        // =========================================================================
        IReadOnlyList<ToolConfig>? tools = null;
        if (toolConfigService != null && toolType != null)
        {
            tools = await toolConfigService.GetToolsByTypeAsync(toolType, ct).ConfigureAwait(false);
            if (tools.Count > 0)
            {
                // 1.1 精准匹配指定的版本名
                if (!string.IsNullOrWhiteSpace(snapshotName))
                {
                    var matchedByName = tools.FirstOrDefault(t => t.VersionName.Equals(snapshotName, StringComparison.OrdinalIgnoreCase));
                    if (matchedByName != null && File.Exists(matchedByName.ExecutablePath))
                    {
                        resolvedEnvVars ??= matchedByName.EnvVariables;
                        return (matchedByName.ExecutablePath, resolvedEnvVars);
                    }
                }

                // 1.2 版本名脱落或未指定 -> 坚决回退到默认工具（自愈回退，防旧死路径强占）
                var defaultTool = tools.FirstOrDefault(t => t.IsDefault && File.Exists(t.ExecutablePath)) ??
                                  tools.FirstOrDefault(t => File.Exists(t.ExecutablePath));
                if (defaultTool != null)
                {
                    if (!string.IsNullOrWhiteSpace(snapshotName))
                    {
                        logger.LogWarning("解释器版本 '{SnapshotName}' 在工具库中已失效，自愈回退到默认工具 '{DefaultName}': {Path}",
                            snapshotName, defaultTool.VersionName, defaultTool.ExecutablePath);
                    }
                    resolvedEnvVars ??= defaultTool.EnvVariables;
                    return (defaultTool.ExecutablePath, resolvedEnvVars);
                }
            }
        }

        // 1.3 工具库无任何有效工具时的快照残留路径兜底
        if (!string.IsNullOrWhiteSpace(snapshotPath) && File.Exists(snapshotPath))
        {
            logger.LogInformation("工具库无有效配置，沿用历史快照有效物理路径: {Path}", snapshotPath);
            return (snapshotPath, resolvedEnvVars);
        }

        // =========================================================================
        // 第 2 级 & 第 3 级: 聚合环境探测服务 (PATH 两段式 + 版本管理器轮询)
        // =========================================================================
        string? candidateCustom = null;
        if (!string.IsNullOrWhiteSpace(snapshotName))
        {
            candidateCustom = snapshotName;
        }
        else if (!string.IsNullOrWhiteSpace(snapshotPath))
        {
            try
            {
                candidateCustom = Path.GetFileName(snapshotPath);
            }
            catch
            {
                // 忽略包含非法路径字符的输入
            }
        }

        var detectedPath = await _detectionService.DetectAsync(normalizedType, candidateCustom, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(detectedPath) && File.Exists(detectedPath))
        {
            logger.LogInformation("环境探测命中解释器: {Path}", detectedPath);
            return (detectedPath, resolvedEnvVars);
        }

        // =========================================================================
        // 终点: 明确失败，抛出带有排查诊断摘要的友好异常
        // =========================================================================
        var diagInfo = new StringBuilder();
        diagInfo.AppendLine($"未检测到「{normalizedType}」可用解释器。");
        diagInfo.AppendLine("排查诊断摘要：");
        diagInfo.AppendLine($"  - [第 1 级 工具库]：{(tools == null || tools.Count == 0 ? "未配置任何工具" : $"已配置 {tools.Count} 个工具但无有效匹配")}");
        diagInfo.AppendLine($"  - [第 2 级 系统 PATH]：已检索自定义候选 '{candidateCustom}' 与内置候选名，未命中");
        var detectorsStr = _detectionService.DetectorNames.Count > 0
            ? string.Join('/', _detectionService.DetectorNames)
            : "多版本环境管理工具";
        diagInfo.AppendLine($"  - [第 3 级 版本工具]：已轮询 {detectorsStr}，未探测到安装实例");
        diagInfo.Append("解决方案：请在桌面端「全局设置 -> 工具配置」中添加对应解释器，或将其安装至系统全局 PATH 中。");

        throw new FileNotFoundException(diagInfo.ToString());
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

    /// <summary>
    /// 对 C# 脚本命令入参进行纯参数校验（无解释器依赖）。
    /// </summary>
    private static CommandResult? PrevalidateCSharpCommand(string command)
    {
        var trimmed = command.Trim();
        bool isSingleLine = !trimmed.Contains('\n') && !trimmed.Contains('\r');
        if (!isSingleLine)
            return null;

        // 1. 若为单行且以 .csx 结尾，明确提示不支持并引导使用 .NET 10 .cs 格式
        if (trimmed.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
        {
            return new CommandResult(-1, string.Empty, "dotnet run 不支持 .csx，请改用 .NET 10 基于文件的应用格式（.cs）。");
        }

        // 2. 若单行以 .cs 结尾，必然期望执行物理文件，若文件不存在则报错
        if (trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(trimmed))
            {
                return new CommandResult(-1, string.Empty, $"脚本文件不存在: {trimmed}");
            }
            return null;
        }

        // 3. 若为单行且为本地已存在的文件，但扩展名非 .cs，明确提示不支持
        if (File.Exists(trimmed))
        {
            return new CommandResult(-1, string.Empty, $"C# 命令类型仅支持 .cs 脚本文件，当前输入: {trimmed}");
        }

        return null;
    }

    /// <summary>
    /// 执行 .NET 10 基于文件的应用（单文件 C# 脚本）。
    /// 若输入为本地存在的 .cs 文件路径，则直接运行该物理文件（归一化为绝对路径）；
    /// 若输入为代码文本，则以 UTF-8（无 BOM）保存至临时目录后通过 dotnet run 执行。
    /// </summary>
    private async Task<CommandResult> ExecuteCSharpAsync(
        string command,
        string interpreterPath,
        string? envVariables,
        CancellationToken ct)
    {
        var trimmed = command.Trim();
        bool isSingleLine = !trimmed.Contains('\n') && !trimmed.Contains('\r');

        string targetScriptPath;
        bool isTempFile = false;

        if (isSingleLine && trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed))
        {
            targetScriptPath = Path.GetFullPath(trimmed);
        }
        else
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TaskScheduler", "scripts");
            Directory.CreateDirectory(tempDir);
            targetScriptPath = Path.Combine(tempDir, $"task_{Guid.NewGuid():N}.cs");
            isTempFile = true;
        }

        try
        {
            if (isTempFile)
            {
                // 统一采用无 BOM UTF-8 保存（写入放在 try 内，确保中途失败或取消时 finally 能够清理残余文件）
                await File.WriteAllTextAsync(targetScriptPath, command, new UTF8Encoding(false), ct).ConfigureAwait(false);
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = !string.IsNullOrWhiteSpace(interpreterPath)
                    ? interpreterPath
                    : (OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(targetScriptPath) ?? Environment.CurrentDirectory
            };

            processStartInfo.Environment["DOTNET_NOLOGO"] = "1";
            processStartInfo.Environment["NO_COLOR"] = "1";
            ApplyEnvironmentVariables(processStartInfo, envVariables);

            processStartInfo.ArgumentList.Add("run");
            processStartInfo.ArgumentList.Add("-v");
            processStartInfo.ArgumentList.Add("quiet");
            processStartInfo.ArgumentList.Add("--file");
            processStartInfo.ArgumentList.Add(targetScriptPath);

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
                return new CommandResult(-1, string.Empty, "启动 dotnet 进程失败。");

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(ct);

            try
            {
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

            logger.LogInformation("C# single-file script executed with exit code: {ExitCode}", process.ExitCode);

            // 若退出码非 0 且检测到低版本 SDK 特征报错，提供友好诊断指引
            bool isLowVersionSdk = process.ExitCode != 0 &&
                ((errorText != null && (
                    (errorText.Contains("--file", StringComparison.OrdinalIgnoreCase) && errorText.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase)) ||
                    errorText.Contains("NETSDK1045", StringComparison.OrdinalIgnoreCase))) ||
                 (outputText != null && (
                    (outputText.Contains("--file", StringComparison.OrdinalIgnoreCase) && outputText.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase)) ||
                    outputText.Contains("NETSDK1045", StringComparison.OrdinalIgnoreCase))));

            if (isLowVersionSdk)
            {
                var tip = "\n\n[提示] 检测到当前使用的 dotnet SDK 可能未达到 .NET 10。" +
                          "基于文件的单文件应用（File-based Apps）需要 .NET 10.0 及以上版本 SDK 支持，" +
                          "请升级 .NET SDK 或通过命令配置全局工具：tscli tool add -t DotNet -n dotnet10 -p <路径> --default";
                errorText = string.IsNullOrEmpty(errorText) ? tip.TrimStart() : errorText + tip;
            }

            return new CommandResult(process.ExitCode, outputText ?? string.Empty, errorText ?? string.Empty);
        }
        finally
        {
            if (isTempFile && File.Exists(targetScriptPath))
            {
                try
                {
                    File.Delete(targetScriptPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "清理 C# 临时文件失败: {Path}", targetScriptPath);
                }
            }
        }
    }

    [GeneratedRegex(@"^(.+?)\s*\((.+)\)$")]
    private static partial Regex InterpreterSnapshotRegex();
}
