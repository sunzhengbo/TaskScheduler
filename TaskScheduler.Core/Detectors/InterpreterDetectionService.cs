using System.Collections.Concurrent;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// 解释器环境探测聚合服务实现。
/// </summary>
public class InterpreterDetectionService(IEnumerable<IVersionManagerDetector>? detectors = null) : IInterpreterDetectionService
{
    private readonly IReadOnlyList<IVersionManagerDetector> _detectors =
        (detectors ?? Enumerable.Empty<IVersionManagerDetector>()).OrderBy(d => d.Order).ToList();
    private readonly ConcurrentDictionary<string, (string? Path, DateTime ExpireAt)> _managerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 已知标准命令类型集合（仅对已知类型预分配并发锁，防止非法类型导致锁字典无界膨胀）。
    /// </summary>
    private static readonly HashSet<string> KnownCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        CommandTypes.PowerShell,
        CommandTypes.Python,
        CommandTypes.NodeJs,
        CommandTypes.Shell,
        CommandTypes.Cmd,
        CommandTypes.VBScript
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _detectLocks = new(
        KnownCommandTypes.ToDictionary(k => k, _ => new SemaphoreSlim(1, 1), StringComparer.OrdinalIgnoreCase),
        StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<string> DetectorNames { get; } =
        (detectors ?? Enumerable.Empty<IVersionManagerDetector>())
        .OrderBy(d => d.Order)
        .Select(d => d.Name)
        .ToList();

    /// <summary>
    /// 全局默认静态单例，过渡兼容老代码与存量单元测试。
    /// 包含全部 7 个标准多版本探测器实现。
    /// </summary>
    private static readonly Lazy<InterpreterDetectionService> _defaultInstance = new(() =>
        new InterpreterDetectionService(new IVersionManagerDetector[]
        {
            new MiseDetector(),
            new ScoopDetector(),
            new UvDetector(),
            new AsdfDetector(),
            new NvmDetector(),
            new FnmDetector(),
            new VoltaDetector()
        }));

    public static InterpreterDetectionService Default => _defaultInstance.Value;

    /// <inheritdoc />
    public string? SearchInPath(string commandType, string? customCandidate = null)
    {
        commandType = CommandTypes.Normalize(commandType);
        var candidates = new List<string>();

        // 1. 第一段：优先尝试用户传入的候选名或文件名
        if (!string.IsNullOrWhiteSpace(customCandidate))
        {
            try
            {
                var trimmed = customCandidate.Trim();
                var fileName = Path.GetFileName(trimmed);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    candidates.Add(fileName);
                }
            }
            catch
            {
                // 忽略包含非法路径字符的输入
            }
        }

        // 2. 第二段：根据语言类型枚举内置标准候选
        switch (commandType)
        {
            case CommandTypes.PowerShell:
                candidates.Add("pwsh.exe");
                candidates.Add("pwsh");
                candidates.Add("powershell.exe");
                candidates.Add("powershell");
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
            {
                return found;
            }
        }

        // 3. 特殊兜底：Windows 环境下若守护进程 PATH 被严重裁剪，检查常用系统级路径
        if (OperatingSystem.IsWindows())
        {
            if (string.Equals(commandType, CommandTypes.PowerShell, StringComparison.OrdinalIgnoreCase))
            {
                var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                var defaultWinPowerShell = Path.Combine(systemRoot, @"System32\WindowsPowerShell\v1.0\powershell.exe");
                if (File.Exists(defaultWinPowerShell))
                {
                    return defaultWinPowerShell;
                }
            }
            else if (string.Equals(commandType, CommandTypes.Cmd, StringComparison.OrdinalIgnoreCase))
            {
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var cmd = Path.Combine(system, "cmd.exe");
                if (File.Exists(cmd))
                {
                    return cmd;
                }
            }
            else if (string.Equals(commandType, CommandTypes.VBScript, StringComparison.OrdinalIgnoreCase))
            {
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var cscript = Path.Combine(system, "cscript.exe");
                if (File.Exists(cscript))
                {
                    return cscript;
                }
            }
            else if (string.Equals(commandType, CommandTypes.Shell, StringComparison.OrdinalIgnoreCase))
            {
                var shellCandidates = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe")
                };
                foreach (var cand in shellCandidates)
                {
                    if (File.Exists(cand)) return cand;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<string?> DetectAsync(string commandType, string? customCandidate = null, CancellationToken ct = default)
    {
        commandType = CommandTypes.Normalize(commandType);

        // 1. 第 2 级: 系统 PATH 实时检索（两段式，无缓存，杜绝候选键冲突）
        var pathResult = SearchInPath(commandType, customCandidate);
        if (!string.IsNullOrWhiteSpace(pathResult))
        {
            return pathResult;
        }

        // 2. 第 3 级: 版本管理器探测器轮询（仅对第 3 级带 5 分钟 TTL 缓存，按 commandType 隔离）
        var now = DateTime.UtcNow;
        if (_managerCache.TryGetValue(commandType, out var entry) && entry.ExpireAt > now && (entry.Path == null || File.Exists(entry.Path)))
        {
            return entry.Path;
        }

        // 并发单飞去重：针对同一个 commandType，并发首次或过期探测时只允许一个执行扫描
        var sem = _detectLocks.GetOrAdd(commandType, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = DateTime.UtcNow;
            if (_managerCache.TryGetValue(commandType, out entry) && entry.ExpireAt > now && (entry.Path == null || File.Exists(entry.Path)))
            {
                return entry.Path;
            }

            string? detected = null;
            foreach (var detector in _detectors)
            {
                ct.ThrowIfCancellationRequested();
                var result = await detector.DetectAsync(commandType, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result) && File.Exists(result))
                {
                    detected = result;
                    break;
                }
            }

            // 3. 官方原生标准系统安装目录兜底（当 PATH 与各版本管理器均未命中时）
            if (string.IsNullOrWhiteSpace(detected) && OperatingSystem.IsWindows())
            {
                detected = DetectStandardSystemInstallPath(commandType);
            }

            _managerCache[commandType] = (detected, now.Add(CacheTtl));
            return detected;
        }
        finally
        {
            sem.Release();
            // 若为非标准已知类型（动态输入或未知语言），探测并写入缓存后及时清理锁实例，彻底杜绝字典无界膨胀
            if (!KnownCommandTypes.Contains(commandType))
            {
                _detectLocks.TryRemove(commandType, out _);
            }
        }
    }

    private static string? DetectStandardSystemInstallPath(string commandType)
    {
        if (string.Equals(commandType, CommandTypes.Python, StringComparison.OrdinalIgnoreCase))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programsPython = Path.Combine(localAppData, "Programs", "Python");
            if (Directory.Exists(programsPython))
            {
                try
                {
                    var vDirs = Directory.EnumerateDirectories(programsPython).OrderByDescendingVersion();
                    foreach (var vDir in vDirs)
                    {
                        var exe = Path.Combine(vDir, "python.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
                catch
                {
                    // 忽略目录枚举权限异常
                }
            }
        }
        else if (string.Equals(commandType, CommandTypes.NodeJs, StringComparison.OrdinalIgnoreCase))
        {
            var standardDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node.exe")
            };
            foreach (var path in standardDirs)
            {
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    private static string? SearchPathEnvironment(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var pathSources = new List<string?> { Environment.GetEnvironmentVariable("PATH") };
        if (OperatingSystem.IsWindows())
        {
            try
            {
                pathSources.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
                pathSources.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            }
            catch
            {
                // 忽略受限权限环境异常
            }
        }

        var allDirs = new List<string>();
        foreach (var pathEnv in pathSources)
        {
            if (string.IsNullOrWhiteSpace(pathEnv)) continue;
            var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            allDirs.AddRange(dirs);
        }

        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(fileName)
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var path in allDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trimmedPath = path.Trim();
            if (string.IsNullOrEmpty(trimmedPath)) continue;

            foreach (var ext in extensions)
            {
                try
                {
                    var fullPath = Path.Combine(trimmedPath, fileName + ext);
                    if (File.Exists(fullPath))
                    {
                        return Path.GetFullPath(fullPath);
                    }
                }
                catch
                {
                    // 忽略非法路径字符
                }
            }
        }

        return null;
    }
}
