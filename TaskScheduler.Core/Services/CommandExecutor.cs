using System.Diagnostics;
using System.Globalization;
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
    static CommandExecutor()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // 忽略注册异常
        }
    }

    private static Encoding GetOemEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    private readonly IInterpreterDetectionService _detectionService = detectionService ?? InterpreterDetectionService.Default;

    public Task<CommandResult> ExecuteCommandAsync(
        CommandModel model,
        CancellationToken ct = default,
        string? envVariables = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ExecuteCommandAsync(
            model.Content,
            model.Type,
            model.InterpreterVersion ?? model.InterpreterPath,
            ct,
            envVariables,
            model.ScriptMode);
    }

    public async Task<CommandResult> ExecuteCommandAsync(
        string command,
        string type,
        string? interpreter = null,
        CancellationToken ct = default,
        string? envVariables = null,
        string? scriptMode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var normalizedType = CommandTypes.Normalize(type);

        // 判定执行模式：显式优先；未提供模式时对存量任务做安全回退（单行且物理文件存在则自动走 File）
        bool isFileMode;
        if (string.Equals(scriptMode, ScriptModes.File, StringComparison.OrdinalIgnoreCase))
        {
            isFileMode = true;
        }
        else if (string.Equals(scriptMode, ScriptModes.Inline, StringComparison.OrdinalIgnoreCase))
        {
            isFileMode = false;
        }
        else
        {
            isFileMode = ScriptModes.InferLegacyMode(normalizedType, command) == ScriptModes.File;
        }

        // 文件模式前置校验
        string? scriptPath = null;
        string? scriptArgs = null;
        if (isFileMode)
        {
            (scriptPath, scriptArgs) = SplitScriptPathAndArgs(command);

            if (normalizedType == CommandTypes.CSharp)
            {
                if (scriptPath.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
                    return new CommandResult(-1, string.Empty, "dotnet run 不支持 .csx，请改用 .NET 10 基于文件的应用格式（.cs）。");
                if (!scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    return new CommandResult(-1, string.Empty, $"C# 命令类型仅支持 .cs 脚本文件，当前输入: {scriptPath}");
            }

            if (!File.Exists(scriptPath))
            {
                return new CommandResult(-1, string.Empty, $"脚本文件不存在: {scriptPath}");
            }
        }
        else if (normalizedType == CommandTypes.CSharp)
        {
            var trimmed = command.Trim();
            bool isSingleLine = !trimmed.Contains('\n') && !trimmed.Contains('\r');
            if (isSingleLine && trimmed.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
            {
                return new CommandResult(-1, string.Empty, "dotnet run 不支持 .csx，请改用 .NET 10 基于文件的应用格式（.cs）。");
            }
        }

        var (resolvedExecutable, resolvedEnvVars) = await ResolveInterpreterAsync(
            normalizedType,
            interpreter,
            envVariables,
            ct).ConfigureAwait(false);

        logger.LogInformation("Executing command (Type: {Type}, Executable: {Executable}, Mode: {Mode})",
            normalizedType, resolvedExecutable, isFileMode ? ScriptModes.File : ScriptModes.Inline);

        if (normalizedType == CommandTypes.VBScript)
        {
            if (!OperatingSystem.IsWindows())
                return new CommandResult(-1, string.Empty, "VBScript is only supported on Windows.");
            return await ExecuteVBScriptAsync(command, resolvedExecutable, resolvedEnvVars, isFileMode, scriptPath, scriptArgs, ct).ConfigureAwait(false);
        }

        if (normalizedType == CommandTypes.CSharp)
        {
            return await ExecuteCSharpAsync(command, resolvedExecutable, resolvedEnvVars, isFileMode, scriptPath, scriptArgs, ct).ConfigureAwait(false);
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = isFileMode && scriptPath != null
                ? (Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory)
                : Environment.CurrentDirectory
        };

        ApplyEnvironmentVariables(processStartInfo, resolvedEnvVars);

        if (isFileMode && scriptPath != null)
        {
            switch (normalizedType)
            {
                case CommandTypes.PowerShell:
                    processStartInfo.StandardOutputEncoding = Encoding.UTF8;
                    processStartInfo.StandardErrorEncoding = Encoding.UTF8;
                    processStartInfo.Environment["NO_COLOR"] = "1";
                    processStartInfo.ArgumentList.Add("-NoProfile");
                    processStartInfo.ArgumentList.Add("-ExecutionPolicy");
                    processStartInfo.ArgumentList.Add("Bypass");
                    processStartInfo.ArgumentList.Add("-File");
                    processStartInfo.ArgumentList.Add(scriptPath);
                    AppendArguments(processStartInfo, scriptArgs);
                    break;

                case CommandTypes.Python:
                case CommandTypes.Shell:
                case CommandTypes.NodeJs:
                    processStartInfo.ArgumentList.Add(scriptPath);
                    AppendArguments(processStartInfo, scriptArgs);
                    break;

                case CommandTypes.Cmd:
                    if (!OperatingSystem.IsWindows())
                        return new CommandResult(-1, string.Empty, "Cmd is only supported on Windows.");
                    processStartInfo.ArgumentList.Add("/C");
                    processStartInfo.ArgumentList.Add(scriptPath);
                    AppendArguments(processStartInfo, scriptArgs);
                    break;

                default:
                    return new CommandResult(-1, string.Empty, $"Unsupported command type: {type}");
            }
        }
        else
        {
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



    private static (string Path, string? Args) SplitScriptPathAndArgs(string input) =>
        ScriptModes.SplitScriptPathAndArgs(input);

    private static void AppendArguments(ProcessStartInfo psi, string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return;
        var tokens = ParseCommandLineArgs(args);
        foreach (var token in tokens)
        {
            psi.ArgumentList.Add(token);
        }
    }

    internal static List<string> ParseCommandLineArgs(string commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return args;

        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;
        int i = 0;

        while (i < commandLine.Length)
        {
            int slashCount = 0;
            while (i < commandLine.Length && commandLine[i] == '\\')
            {
                slashCount++;
                i++;
            }

            if (i < commandLine.Length && commandLine[i] == '"')
            {
                hasToken = true;
                current.Append('\\', slashCount / 2);
                if (slashCount % 2 != 0)
                {
                    current.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                i++;
            }
            else
            {
                if (slashCount > 0)
                {
                    hasToken = true;
                    current.Append('\\', slashCount);
                }

                if (i >= commandLine.Length)
                    break;

                char c = commandLine[i];
                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasToken)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                }
                else
                {
                    hasToken = true;
                    current.Append(c);
                }
                i++;
            }
        }

        if (hasToken)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    private async Task<CommandResult> ExecuteVBScriptAsync(
        string command,
        string interpreterPath,
        string? envVariables,
        bool isFileMode,
        string? scriptPath,
        string? scriptArgs,
        CancellationToken ct)
    {
        string targetScriptPath;
        bool isTempFile = false;

        if (isFileMode && scriptPath != null)
        {
            targetScriptPath = scriptPath;
        }
        else
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TaskScheduler", "scripts");
            Directory.CreateDirectory(tempDir);
            targetScriptPath = Path.Combine(tempDir, $"task_{Guid.NewGuid():N}.vbs");
            isTempFile = true;
        }

        try
        {
            if (isTempFile)
            {
                await File.WriteAllTextAsync(targetScriptPath, command, Encoding.Default, ct).ConfigureAwait(false);
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = !string.IsNullOrWhiteSpace(interpreterPath) ? interpreterPath : "cscript.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = GetOemEncoding(),
                StandardErrorEncoding = GetOemEncoding(),
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(targetScriptPath) ?? Environment.CurrentDirectory
            };

            ApplyEnvironmentVariables(processStartInfo, envVariables);
            processStartInfo.ArgumentList.Add("//Nologo");
            processStartInfo.ArgumentList.Add(targetScriptPath);
            if (isFileMode)
            {
                AppendArguments(processStartInfo, scriptArgs);
            }

            using var process = new Process { StartInfo = processStartInfo };
            if (!process.Start())
                return new CommandResult(-1, string.Empty, "Failed to start VBScript process.");

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

            logger.LogInformation("VBScript executed with exit code: {ExitCode}", process.ExitCode);
            return new CommandResult(process.ExitCode, outputText ?? string.Empty, errorText ?? string.Empty);
        }
        finally
        {
            if (isTempFile && File.Exists(targetScriptPath))
            {
                try { File.Delete(targetScriptPath); } catch (Exception ex) { logger.LogWarning(ex, "清理 VBScript 临时文件失败: {Path}", targetScriptPath); }
            }
        }
    }

    private async Task<CommandResult> ExecuteCSharpAsync(
        string command,
        string interpreterPath,
        string? envVariables,
        bool isFileMode,
        string? scriptPath,
        string? scriptArgs,
        CancellationToken ct)
    {
        string targetScriptPath;
        bool isTempFile = false;

        if (isFileMode && scriptPath != null)
        {
            targetScriptPath = scriptPath;
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
            if (isFileMode && !string.IsNullOrWhiteSpace(scriptArgs))
            {
                processStartInfo.ArgumentList.Add("--");
                AppendArguments(processStartInfo, scriptArgs);
            }

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
