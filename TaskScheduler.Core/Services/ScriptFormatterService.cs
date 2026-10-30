using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// Formats inline scripts with Roslyn or locally installed external formatters.
/// </summary>
public sealed partial class ScriptFormatterService(
    ISettingsService settingsService,
    IToolConfigService toolConfigService,
    IInterpreterDetectionService interpreterDetectionService) : IScriptFormatterService
{
    private const string RoslynToolName = "Roslyn";
    private const string RoslynVersion = "5.9.0";
    private const string DirectivePlaceholderPrefix = "//__TS_DIRECTIVE_";
    private static readonly Version MinimumPrettierVersion = Version.Parse(ScriptFormatterDefaults.MinimumPrettierVersion);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 终止进程后的等待上限。与 <see cref="ProbeTimeout"/> 语义不同（后者是「探测耗时预算」），
    /// 因此独立配置，调整探测超时时不会意外改变终止等待时长。
    /// </summary>
    private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 探测结果的有效期。缓存键由 <see cref="BuildResolutionCacheKey"/> 构造，只包含真正影响探测结论的
    /// 字段（语言、解释器路径、四个 formatter 路径）；超时只作用在格式化执行阶段、不参与探测，
    /// 因此修改超时既不需要也无法让缓存失效。
    /// </summary>
    private static readonly TimeSpan ResolutionCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 探测结果短期缓存：切换命令类型或解释器时 UI 会反复询问可用性，
    /// 而每次探测都要启动 1-4 个子进程，缓存可避免重复开销。
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedResolution> _resolutionCache = new(StringComparer.Ordinal);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public async Task<ScriptFormatterSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var json = await settingsService.GetValueAsync(ScriptFormatterSettings.StorageKey, ct).ConfigureAwait(false);
        return ScriptFormatterSettings.FromJson(json);
    }

    public async Task SaveSettingsAsync(ScriptFormatterSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // 落盘前统一折叠空白路径，避免调用方（尤其是直接构造对象的 UI）把 "" 写进设置，
        // 否则 show --json 会输出空串，与「未配置」的 null 语义不一致。
        settings.NormalizePaths();
        settings.Validate();
        await settingsService.SetValueAsync(ScriptFormatterSettings.StorageKey, settings.ToJson(), ct).ConfigureAwait(false);
        _resolutionCache.Clear();
    }

    public async Task<ScriptFormatterCapability> GetCapabilityAsync(
        string commandType,
        string? interpreterPath = null,
        CancellationToken ct = default)
    {
        var language = CommandTypes.Normalize(commandType);
        var settings = await GetSettingsAsync(ct).ConfigureAwait(false);
        var resolution = await ResolveCachedAsync(language, interpreterPath, settings, ct).ConfigureAwait(false);
        return resolution.Capability;
    }

    public async Task<ScriptFormatterResult> FormatAsync(
        string commandType,
        string source,
        string? interpreterPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var language = CommandTypes.Normalize(commandType);
        var settings = await GetSettingsAsync(ct).ConfigureAwait(false);
        var resolution = await ResolveCachedAsync(language, interpreterPath, settings, ct).ConfigureAwait(false);

        if (!resolution.Capability.Supported)
        {
            return Failure($"{language} 暂不支持格式化。", null, null);
        }

        if (language == CommandTypes.CSharp)
        {
            if (!resolution.Capability.Available)
            {
                return Failure(resolution.Capability.Reason ?? "Roslyn formatter 不可用。", RoslynToolName, RoslynVersion);
            }

            try
            {
                var output = await Task.Run(() => FormatCSharp(source, ct), ct).ConfigureAwait(false);
                return Success(PreserveLineEndings(source, output), RoslynToolName, RoslynVersion);
            }
            catch (InvalidDataException ex)
            {
                return Failure(ex.Message, RoslynToolName, RoslynVersion);
            }
        }

        if (!resolution.Capability.Available || resolution.Invocation is null)
        {
            return Failure(resolution.Capability.Reason ?? "找不到可用的格式化工具。",
                resolution.Capability.Tool, resolution.Capability.Version);
        }

        var run = await RunProcessAsync(
            resolution.Invocation.ExecutablePath,
            resolution.Invocation.Arguments,
            source,
            resolution.Invocation.Environment,
            TimeSpan.FromSeconds(settings.TimeoutSeconds),
            ct).ConfigureAwait(false);

        if (run.Canceled)
        {
            ct.ThrowIfCancellationRequested();
            return Failure("格式化已取消。", resolution.Capability.Tool, resolution.Capability.Version);
        }

        if (run.TimedOut)
        {
            return Failure($"格式化超过 {settings.TimeoutSeconds} 秒，已停止运行。",
                resolution.Capability.Tool, resolution.Capability.Version);
        }

        if (run.StartError != null)
        {
            return Failure(run.StartError, resolution.Capability.Tool, resolution.Capability.Version);
        }

        if (run.ExitCode != 0)
        {
            var detail = FirstNonEmptyLine(run.StandardError) ?? FirstNonEmptyLine(run.StandardOutput);
            return Failure(detail ?? $"格式化工具退出码为 {run.ExitCode}。",
                resolution.Capability.Tool, resolution.Capability.Version);
        }

        return Success(PreserveLineEndings(source, run.StandardOutput),
            resolution.Capability.Tool, resolution.Capability.Version);
    }

    /// <summary>
    /// 带短期缓存的探测入口。缓存键只取影响探测结论的字段（见 <see cref="BuildResolutionCacheKey"/>），
    /// 因此改 formatter 路径或切换解释器会自然产生新键，不会读到过期结论。
    /// </summary>
    private async Task<FormatterResolution> ResolveCachedAsync(
        string language,
        string? interpreterPath,
        ScriptFormatterSettings settings,
        CancellationToken ct)
    {
        var key = BuildResolutionCacheKey(language, interpreterPath, settings);
        var now = DateTimeOffset.UtcNow;
        if (_resolutionCache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Resolution;
        }

        var resolution = await ResolveAsync(language, interpreterPath, settings, ct).ConfigureAwait(false);

        // 有效期必须从「探测完成」时刻起算：一次探测要启动 1-4 个子进程，可能耗时数秒，
        // 若沿用进入方法时的时间戳，实际缓存时长会被探测耗时吃掉（甚至写入即过期）。
        var completedAt = DateTimeOffset.UtcNow;
        // 缓存规模 = 语言 × 解释器 × 设置组合，量级很小，写入时顺手清掉过期项即可避免只增不减
        PruneExpiredResolutions(completedAt);
        _resolutionCache[key] = new CachedResolution(resolution, completedAt + ResolutionCacheTtl);
        return resolution;
    }

    /// <summary>
    /// 缓存键只拼接真正影响探测结论的字段（语言、解释器、四个 formatter 路径），
    /// 不再整体序列化设置，避免为了取键多做一次 JSON 序列化。
    /// </summary>
    private static string BuildResolutionCacheKey(
        string language,
        string? interpreterPath,
        ScriptFormatterSettings settings) =>
        string.Join('\u0001',
            language,
            interpreterPath ?? string.Empty,
            settings.BlackExecutablePath ?? string.Empty,
            settings.PsscriptAnalyzerModulePath ?? string.Empty,
            settings.ShfmtExecutablePath ?? string.Empty,
            settings.PrettierPackagePath ?? string.Empty);

    private void PruneExpiredResolutions(DateTimeOffset now)
    {
        foreach (var entry in _resolutionCache)
        {
            if (entry.Value.ExpiresAt <= now)
            {
                _resolutionCache.TryRemove(entry.Key, out _);
            }
        }
    }

    private async Task<FormatterResolution> ResolveAsync(
        string language,
        string? interpreterPath,
        ScriptFormatterSettings settings,
        CancellationToken ct)
    {
        if (language == CommandTypes.CSharp)
        {
            return Available(language, RoslynToolName, RoslynVersion);
        }

        if (language == CommandTypes.Python)
        {
            // 显式配置了 Black 时直接使用，不再探测解释器，避免多启动一次子进程
            if (!string.IsNullOrWhiteSpace(settings.BlackExecutablePath))
            {
                var blackPath = ResolveExecutable(settings.BlackExecutablePath);
                if (blackPath is null)
                {
                    return Unavailable(language, "Black", $"配置的 Black 路径不存在：{settings.BlackExecutablePath}");
                }

                var probe = await RunProcessAsync(blackPath, ["--version"], null, null, ProbeTimeout, ct)
                    .ConfigureAwait(false);
                var version = ParseBlackVersion(probe.StandardOutput + probe.StandardError);
                return probe.ExitCode == 0 && version != null
                    ? Available(language, "Black", version, blackPath, ["-"])
                    : Unavailable(language, "Black", "配置的 Black 无法启动或版本信息无效。");
            }

            var python = await ResolveInterpreterAsync(language, interpreterPath, ["python", "python3"], ct)
                .ConfigureAwait(false);
            if (python != null)
            {
                var pythonVersion = await ProbePythonVersionAsync(python, ct).ConfigureAwait(false);
                var black = await RunProcessAsync(python, ["-m", "black", "--version"], null, null, ProbeTimeout, ct)
                    .ConfigureAwait(false);
                var blackVersion = ParseBlackVersion(black.StandardOutput + black.StandardError);
                if (pythonVersion != null && pythonVersion >= new Version(3, 10) && black.ExitCode == 0 && blackVersion != null)
                {
                    return Available(language, "Black", blackVersion, python, ["-m", "black", "-"],
                        new Dictionary<string, string>
                        {
                            ["PYTHONUTF8"] = "1",
                            ["PYTHONIOENCODING"] = "utf-8"
                        });
                }
            }

            var blackPathOnPath = FindExecutable("black");
            if (blackPathOnPath != null)
            {
                var probe = await RunProcessAsync(blackPathOnPath, ["--version"], null, null, ProbeTimeout, ct)
                    .ConfigureAwait(false);
                var version = ParseBlackVersion(probe.StandardOutput + probe.StandardError);
                if (probe.ExitCode == 0 && version != null)
                {
                    return Available(language, "Black", version, blackPathOnPath, ["-"]);
                }
            }

            return Unavailable(language, "Black", "未找到可用 Black。请在任务选定的 Python 环境安装 Black，或配置 Black 可执行文件路径。");
        }

        if (language == CommandTypes.PowerShell)
        {
            var powershell = await ResolveInterpreterAsync(language, interpreterPath,
                OperatingSystem.IsWindows() ? ["pwsh", "powershell"] : ["pwsh"], ct).ConfigureAwait(false);
            if (powershell is null)
            {
                return Unavailable(language, "PSScriptAnalyzer", "未找到 PowerShell 解释器。");
            }

            var probe = await ProbePsscriptAnalyzerAsync(powershell, settings.PsscriptAnalyzerModulePath, ct)
                .ConfigureAwait(false);
            if (!probe.Available)
            {
                return Unavailable(language, "PSScriptAnalyzer",
                    probe.Error ?? "当前 PowerShell 环境未安装 PSScriptAnalyzer。");
            }

            var arguments = PowerShellArguments(CreatePssFormatterScript());
            var environment = new Dictionary<string, string>
            {
                ["TASKSCHEDULER_PSSA_MODULE_PATH"] = settings.PsscriptAnalyzerModulePath ?? string.Empty
            };
            return Available(language, "PSScriptAnalyzer", probe.ModuleVersion!, powershell, arguments, environment);
        }

        if (language == CommandTypes.Shell)
        {
            var shfmt = !string.IsNullOrWhiteSpace(settings.ShfmtExecutablePath)
                ? ResolveExecutable(settings.ShfmtExecutablePath)
                : FindExecutable("shfmt");
            if (shfmt is null)
            {
                return Unavailable(language, "shfmt", "未找到 shfmt。请配置 shfmt 可执行文件路径或将其加入 PATH。");
            }

            var probe = await RunProcessAsync(shfmt, ["--version"], null, null, ProbeTimeout, ct).ConfigureAwait(false);
            var version = FirstNonEmptyLine(probe.StandardOutput + probe.StandardError);
            return probe.ExitCode == 0 && version != null
                ? Available(language, "shfmt", version, shfmt, ["-ln=bash", "-"])
                : Unavailable(language, "shfmt", "shfmt 无法启动或版本信息无效。");
        }

        if (language == CommandTypes.NodeJs)
        {
            var node = await ResolveInterpreterAsync(language, interpreterPath, ["node"], ct).ConfigureAwait(false);
            if (node is null)
            {
                return Unavailable(language, "Prettier", "未找到 Node.js 解释器。");
            }

            var nodeProbe = await RunProcessAsync(node, ["--version"], null, null, ProbeTimeout, ct).ConfigureAwait(false);
            var nodeVersion = ParseVersion(nodeProbe.StandardOutput + nodeProbe.StandardError);
            if (nodeProbe.ExitCode != 0 || nodeVersion is null || nodeVersion.Major < 22)
            {
                return Unavailable(language, "Prettier",
                    $"Prettier {ScriptFormatterDefaults.MinimumPrettierVersion} 需要 Node.js 22 或更高版本。");
            }

            var prettierCli = ResolvePrettierCli(settings.PrettierPackagePath, node);
            if (prettierCli is null)
            {
                return Unavailable(language, "Prettier", "未找到 Prettier 包。请配置包目录/CLI 路径，或将 Prettier 安装到 Node 环境并加入 PATH。");
            }

            var prettierProbe = await RunProcessAsync(node, [prettierCli, "--version"], null, null, ProbeTimeout, ct)
                .ConfigureAwait(false);
            var prettierVersion = FirstNonEmptyLine(prettierProbe.StandardOutput + prettierProbe.StandardError);
            if (prettierProbe.ExitCode != 0 || string.IsNullOrWhiteSpace(prettierVersion) ||
                !IsSupportedPrettierVersion(prettierVersion))
            {
                return Unavailable(language, "Prettier",
                    $"需要 Prettier {ScriptFormatterDefaults.MinimumPrettierVersion} 或更高版本，当前检测到 {(string.IsNullOrWhiteSpace(prettierVersion) ? "未知版本" : prettierVersion)}。");
            }

            return Available(language, "Prettier", prettierVersion, node, [prettierCli, "--stdin-filepath", ".js"]);
        }

        return Unsupported(language);
    }

    private async Task<string?> ResolveInterpreterAsync(
        string language,
        string? preferredPath,
        string[] pathCandidates,
        CancellationToken ct)
    {
        var candidates = new List<string>();
        AddExecutable(candidates, preferredPath);

        var toolType = CommandTypes.MapToToolType(language);
        if (toolType != null)
        {
            var tool = await toolConfigService.GetDefaultToolAsync(toolType, ct).ConfigureAwait(false);
            AddExecutable(candidates, tool?.ExecutablePath);
        }

        var detected = await interpreterDetectionService.DetectAsync(language, ct: ct).ConfigureAwait(false);
        AddExecutable(candidates, detected);
        foreach (var candidate in pathCandidates)
        {
            AddExecutable(candidates, FindExecutable(candidate));
        }

        return candidates.FirstOrDefault();
    }

    private async Task<Version?> ProbePythonVersionAsync(string python, CancellationToken ct)
    {
        var probe = await RunProcessAsync(python, ["--version"], null, null, ProbeTimeout, ct).ConfigureAwait(false);
        return ParseVersion(probe.StandardOutput + probe.StandardError);
    }

    private async Task<PssProbe> ProbePsscriptAnalyzerAsync(string powershell, string? modulePath, CancellationToken ct)
    {
        const string probeScript = "$ErrorActionPreference='Stop'; $utf8=[System.Text.UTF8Encoding]::new($false); [Console]::OutputEncoding=$utf8; $p=$PSVersionTable.PSVersion.ToString(); $path=$env:TASKSCHEDULER_PSSA_MODULE_PATH; if($path){Import-Module -Name $path -ErrorAction Stop}else{$m=Get-Module -ListAvailable -Name PSScriptAnalyzer | Sort-Object Version -Descending | Select-Object -First 1; if(-not $m){throw 'PSScriptAnalyzer not installed'}; Import-Module -Name $m.Path -ErrorAction Stop}; $m=Get-Module PSScriptAnalyzer | Select-Object -First 1; [Console]::Out.Write($p+'|'+$m.Version.ToString())";
        var env = new Dictionary<string, string>
        {
            ["TASKSCHEDULER_PSSA_MODULE_PATH"] = modulePath ?? string.Empty
        };
        var run = await RunProcessAsync(powershell, PowerShellArguments(probeScript), null, env, ProbeTimeout, ct)
            .ConfigureAwait(false);
        if (run.ExitCode != 0)
        {
            return new PssProbe(false, null, FirstNonEmptyLine(run.StandardError) ?? "PowerShell 模块探测失败。");
        }

        var parts = run.StandardOutput.Trim().Split('|', 2);
        if (parts.Length != 2 || !Version.TryParse(parts[0], out var psVersion) || !IsSupportedPowerShell(psVersion))
        {
            return new PssProbe(false, null, "PowerShell 版本不兼容；需要 Windows PowerShell 5.1 或 PowerShell 7.2.11 及以上。");
        }

        return new PssProbe(true, parts[1], null);
    }

    private static string CreatePssFormatterScript() =>
        "$ErrorActionPreference='Stop'; $utf8=[System.Text.UTF8Encoding]::new($false); [Console]::InputEncoding=$utf8; [Console]::OutputEncoding=$utf8; $OutputEncoding=$utf8; $path=$env:TASKSCHEDULER_PSSA_MODULE_PATH; if($path){Import-Module -Name $path -ErrorAction Stop}else{$m=Get-Module -ListAvailable -Name PSScriptAnalyzer | Sort-Object Version -Descending | Select-Object -First 1; if(-not $m){throw 'PSScriptAnalyzer not installed'}; Import-Module -Name $m.Path -ErrorAction Stop}; $source=[Console]::In.ReadToEnd(); $formatted=Invoke-Formatter -ScriptDefinition $source -ErrorAction Stop; [Console]::Out.Write([string]$formatted)";

    private static string[] PowerShellArguments(string script) => ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script];

    private static string? ResolvePrettierCli(string? configuredPath, string nodePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = ResolvePackageCli(configuredPath);
            if (configured != null)
            {
                return configured;
            }
        }

        var nodeDirectory = Path.GetDirectoryName(nodePath);
        // 只信任：显式配置、应用目录、Node 安装目录。不再使用 Environment.CurrentDirectory，
        // 避免工作目录中的 node_modules 被意外（或恶意）当成格式化器执行。
        var localCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "node_modules", "prettier", "bin", "prettier.cjs"),
            nodeDirectory is null ? string.Empty : Path.Combine(nodeDirectory, "node_modules", "prettier", "bin", "prettier.cjs")
        };

        foreach (var path in localCandidates)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        var command = FindPrettierCommand();
        if (command is null)
        {
            return null;
        }

        if (Path.GetExtension(command).Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(command).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var globalCandidate = Path.Combine(Path.GetDirectoryName(command) ?? string.Empty,
                "node_modules", "prettier", "bin", "prettier.cjs");
            return File.Exists(globalCandidate) ? Path.GetFullPath(globalCandidate) : null;
        }

        try
        {
            var target = new FileInfo(command).ResolveLinkTarget(true)?.FullName;
            if (target != null && File.Exists(target))
            {
                return target;
            }
        }
        catch (IOException)
        {
            // A non-symlink executable can still be a valid JavaScript CLI path.
        }

        return Path.GetExtension(command).Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
               Path.GetExtension(command).Equals(".js", StringComparison.OrdinalIgnoreCase)
            ? command
            : null;
    }

    private static string? FindPrettierCommand()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var command = Path.Combine(directory.Trim('"'), OperatingSystem.IsWindows() ? "prettier.cmd" : "prettier");
            if (File.Exists(command))
            {
                return Path.GetFullPath(command);
            }
        }

        return null;
    }

    private static string? ResolvePackageCli(string path)
    {
        if (Directory.Exists(path))
        {
            var cli = Path.Combine(path, "bin", "prettier.cjs");
            return File.Exists(cli) ? Path.GetFullPath(cli) : null;
        }

        if (File.Exists(path) && Path.GetExtension(path) is ".cjs" or ".js")
        {
            return Path.GetFullPath(path);
        }

        return null;
    }

    private async Task<ProcessResult> RunProcessAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? input,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, "", "", false, false, "无法启动格式化工具。");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new ProcessResult(-1, "", "", false, false, $"无法启动格式化工具：{ex.Message}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            if (input != null)
            {
                try
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // A formatter may close stdin early when it rejects its input.
                }
            }
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // 进程可能已提前退出，关闭 stdin 失败不影响退出码与输出读取
            }

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and Kill.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process may already be exiting; wait below and report cancellation/timeout.
            }

            // 终止后仍需有界等待：进程若无法退出，无超时等待会让格式化调用永久挂起
            if (!await WaitForExitBoundedAsync(process, KillWaitTimeout).ConfigureAwait(false))
            {
                // 输出流可能永远不会有结束信号，放弃读取并显式观察异常，避免未观察任务异常
                ObserveFault(stdoutTask);
                ObserveFault(stderrTask);
                var canceledWithoutExit = ct.IsCancellationRequested;
                return new ProcessResult(-1, string.Empty, string.Empty, !canceledWithoutExit, canceledWithoutExit,
                    "格式化工具未能退出，已放弃等待其输出。");
            }

            var canceled = ct.IsCancellationRequested;
            return new ProcessResult(-1, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false),
                !canceled, canceled, null);
        }

        return new ProcessResult(process.ExitCode,
            await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), false, false, null);
    }

    private static async Task<bool> WaitForExitBoundedAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void ObserveFault(Task task) =>
        task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            System.Threading.Tasks.TaskScheduler.Default);

    internal static string FormatCSharp(string source, CancellationToken ct)
    {
        // `#:package` / `#:sdk` 等「基于文件的应用」指令不是合法 C# 语法，直接解析会被判为语法错误，
        // 导致本应用模板生成的脚本无法格式化。这里先整体替换成占位注释（保持行数不变，错误行号仍准确），
        // 格式化完成后再逐行还原。
        var directives = new List<string>();
        var maskedSource = DirectiveLineRegex().Replace(source, match =>
        {
            directives.Add(match.Groups["directive"].Value);
            return DirectivePlaceholderPrefix + directives.Count.ToString(CultureInfo.InvariantCulture) + "__";
        });

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, kind: SourceCodeKind.Script);
        var tree = CSharpSyntaxTree.ParseText(maskedSource, parseOptions, cancellationToken: ct);
        var diagnostics = tree.GetDiagnostics(ct).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length > 0)
        {
            var first = diagnostics[0];
            var line = first.Location.GetLineSpan().StartLinePosition.Line + 1;
            throw new InvalidDataException($"C# 脚本语法错误（第 {line} 行）：{first.GetMessage(CultureInfo.CurrentCulture)}");
        }

        using var workspace = new AdhocWorkspace();
        var root = tree.GetRoot(ct);
        var formatted = Formatter.Format(root, workspace, cancellationToken: ct).ToFullString();
        return directives.Count == 0 ? formatted : RestoreDirectives(formatted, directives);
    }

    private static string RestoreDirectives(string formatted, List<string> directives) =>
        DirectivePlaceholderRegex().Replace(formatted, match =>
            int.TryParse(match.Groups["index"].Value, CultureInfo.InvariantCulture, out var index)
            && index >= 1 && index <= directives.Count
                ? directives[index - 1].Trim()
                : match.Value);

    internal static string PreserveLineEndings(string source, string formatted)
    {
        var crlfCount = 0;
        var lfCount = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n')
            {
                crlfCount++;
                index++;
            }
            else if (source[index] == '\n' || source[index] == '\r')
            {
                lfCount++;
            }
        }

        var lineEnding = crlfCount > lfCount ? "\r\n" : "\n";
        return AnyLineEndingRegex().Replace(formatted, "\n").Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static void AddExecutable(List<string> candidates, string? path)
    {
        var resolved = ResolveExecutable(path);
        if (resolved != null && !candidates.Contains(resolved, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(resolved);
        }
    }

    private static string? ResolveExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        if (Path.HasExtension(path) || path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar))
        {
            return null;
        }

        return FindExecutable(path);
    }

    private static string? FindExecutable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (File.Exists(name))
        {
            return Path.GetFullPath(name);
        }

        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".com" } : new[] { string.Empty };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim('"'), name + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static Version? ParseVersion(string text)
    {
        var match = VersionRegex().Match(text);
        return match.Success && Version.TryParse(match.Value, out var version) ? version : null;
    }

    internal static string? ParseBlackVersion(string text)
    {
        var match = BlackVersionRegex().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static bool IsSupportedPowerShell(Version version) =>
        (version.Major == 5 && version.Minor >= 1) ||
        version.Major > 7 ||
        (version.Major == 7 && (version.Minor > 2 || (version.Minor == 2 && version.Build >= 11)));

    /// <summary>
    /// 按「最低版本」而非精确版本判断 Prettier 可用性，避免补丁版本升级后被误判为不可用。
    /// </summary>
    internal static bool IsSupportedPrettierVersion(string? version) =>
        TryParseVersion(version) is { } parsed && parsed >= MinimumPrettierVersion;

    private static Version? TryParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static ScriptFormatterResult Success(string output, string? tool, string? version) =>
        new(true, output, null, tool, version);

    private static ScriptFormatterResult Failure(string error, string? tool, string? version) =>
        new(false, null, error, tool, version);

    /// <summary>进程内格式化器（如 Roslyn）没有外部可执行文件，不构造调用参数。</summary>
    private static FormatterResolution Available(string language, string tool, string version) =>
        new(new ScriptFormatterCapability(language, true, true, tool, version, null), null);

    private static FormatterResolution Available(string language, string tool, string version, string executable,
        IReadOnlyList<string>? arguments = null, IReadOnlyDictionary<string, string>? environment = null) =>
        new(new ScriptFormatterCapability(language, true, true, tool, version, null),
            new FormatterInvocation(executable, arguments ?? Array.Empty<string>(), environment));

    private static FormatterResolution Unavailable(string language, string tool, string reason) =>
        new(new ScriptFormatterCapability(language, true, false, tool, null, reason), null);

    private static FormatterResolution Unsupported(string language) =>
        new(new ScriptFormatterCapability(language, false, false, null, null, $"{language} 暂不支持格式化。"), null);

    private static string? FirstNonEmptyLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    [GeneratedRegex("\\d+\\.\\d+(?:\\.\\d+)?")]
    private static partial Regex VersionRegex();

    /// <summary>
    /// Black 的 <c>--version</c> 输出形如 <c>black, 24.4.2 (compiled: yes)</c>，
    /// 版本号前是逗号而非空白，因此分隔符必须同时接受逗号与空白。
    /// </summary>
    [GeneratedRegex(@"(?i)black[,\s]+([0-9]+\.[0-9]+(?:\.[0-9]+)?)")]
    private static partial Regex BlackVersionRegex();

    [GeneratedRegex("\\r\\n|\\r|\\n")]
    private static partial Regex AnyLineEndingRegex();

    [GeneratedRegex(@"(?m)^[ \t]*(?<directive>#:[^\r\n]*)")]
    private static partial Regex DirectiveLineRegex();

    [GeneratedRegex(@"//__TS_DIRECTIVE_(?<index>\d+)__")]
    private static partial Regex DirectivePlaceholderRegex();

    private sealed record CachedResolution(FormatterResolution Resolution, DateTimeOffset ExpiresAt);

    private sealed record FormatterInvocation(
        string ExecutablePath,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string>? Environment);

    private sealed record FormatterResolution(
        ScriptFormatterCapability Capability,
        FormatterInvocation? Invocation);

    private sealed record PssProbe(bool Available, string? ModuleVersion, string? Error);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut,
        bool Canceled,
        string? StartError);
}
