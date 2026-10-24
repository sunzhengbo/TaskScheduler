using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// asdf 跨平台多版本管理工具探测器。
/// 优先级: 40
/// </summary>
public class AsdfDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "asdf";
    public override int Order => 40;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        var asdfDir = RootDirOverride;
        if (string.IsNullOrWhiteSpace(asdfDir))
        {
            asdfDir = EnvGetter("ASDF_DATA_DIR");
            if (string.IsNullOrWhiteSpace(asdfDir))
            {
                var home = GetHomeDirectory();
                var candidate = Path.Combine(home, ".asdf");
                if (Directory.Exists(candidate))
                {
                    asdfDir = candidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(asdfDir) || !Directory.Exists(asdfDir))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var plugins = MapCommandTypeToPluginsAndExe(commandType);
        if (plugins.Length == 0)
        {
            return ValueTask.FromResult<string?>(null);
        }

        foreach (var (pluginName, exeBaseName) in plugins)
        {
            var exeName = EnsureExeExtension(exeBaseName);

            // 1. 检索 installs/<plugin>/ 下的版本目录
            var installsDir = Path.Combine(asdfDir, "installs", pluginName);
            if (Directory.Exists(installsDir))
            {
                try
                {
                    var versionDirs = Directory.GetDirectories(installsDir);
                    foreach (var dir in versionDirs.OrderByDescendingVersion())
                    {
                        var binCandidate = Path.Combine(dir, "bin", exeName);
                        if (File.Exists(binCandidate)) return ValueTask.FromResult<string?>(binCandidate);

                        var rootCandidate = Path.Combine(dir, exeName);
                        if (File.Exists(rootCandidate)) return ValueTask.FromResult<string?>(rootCandidate);
                    }
                }
                catch
                {
                    // 忽略文件枚举权限异常
                }
            }

            // 2. 检索 shims/<exe>
            var shimPath = Path.Combine(asdfDir, "shims", exeName);
            if (File.Exists(shimPath))
            {
                return ValueTask.FromResult<string?>(shimPath);
            }
        }

        return ValueTask.FromResult<string?>(null);
    }

    private static (string PluginName, string ExeName)[] MapCommandTypeToPluginsAndExe(string commandType)
    {
        return commandType switch
        {
            CommandTypes.Python => [("python", "python")],
            CommandTypes.NodeJs => [("nodejs", "node")],
            CommandTypes.PowerShell => [("powershell-core", "pwsh"), ("pwsh", "pwsh")],
            _ => []
        };
    }
}
