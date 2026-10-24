using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// Mise (rtx) 多版本管理工具探测器。
/// 优先级: 10
/// </summary>
public class MiseDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "mise";
    public override int Order => 10;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        var miseDir = RootDirOverride;
        if (string.IsNullOrWhiteSpace(miseDir))
        {
            miseDir = EnvGetter("MISE_DATA_DIR");
            if (string.IsNullOrWhiteSpace(miseDir))
            {
                var home = GetHomeDirectory();
                var candidate = Path.Combine(home, ".local", "share", "mise");
                if (Directory.Exists(candidate))
                {
                    miseDir = candidate;
                }
                else
                {
                    var localAppData = EnvGetter("LOCALAPPDATA");
                    if (!string.IsNullOrWhiteSpace(localAppData))
                    {
                        var winCandidate = Path.Combine(localAppData, "mise");
                        if (Directory.Exists(winCandidate))
                        {
                            miseDir = winCandidate;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(miseDir) || !Directory.Exists(miseDir))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var (toolName, exeName) = MapCommandTypeToToolAndExe(commandType);
        if (toolName == null || exeName == null)
        {
            return ValueTask.FromResult<string?>(null);
        }

        // 1. 优先检索 installs/<tool>/ 下各版本安装目录
        var installsDir = Path.Combine(miseDir, "installs", toolName);
        if (Directory.Exists(installsDir))
        {
            // 优先检查 latest 软链接/目录
            var latestDir = Path.Combine(installsDir, "latest");
            var latestBin = FindExecutableInVersionDir(latestDir, exeName);
            if (latestBin != null) return ValueTask.FromResult<string?>(latestBin);

            // 倒序枚举版本目录（优先选择最高版本）
            try
            {
                var versionDirs = Directory.GetDirectories(installsDir);
                foreach (var dir in versionDirs.OrderByDescendingVersion())
                {
                    var candidate = FindExecutableInVersionDir(dir, exeName);
                    if (candidate != null) return ValueTask.FromResult<string?>(candidate);
                }
            }
            catch
            {
                // 忽略文件枚举权限异常
            }
        }

        // 2. 检查 shims/<exe>
        var shimExe = EnsureExeExtension(exeName);
        var shimPath = Path.Combine(miseDir, "shims", shimExe);
        if (File.Exists(shimPath))
        {
            return ValueTask.FromResult<string?>(shimPath);
        }

        return ValueTask.FromResult<string?>(null);
    }

    private static (string? ToolName, string? ExeName) MapCommandTypeToToolAndExe(string commandType)
    {
        return commandType switch
        {
            CommandTypes.PowerShell => ("powershell", "pwsh"),
            CommandTypes.Python => ("python", "python"),
            CommandTypes.NodeJs => ("node", "node"),
            CommandTypes.CSharp => ("dotnet", "dotnet"),
            _ => (null, null)
        };
    }

    private static string? FindExecutableInVersionDir(string versionDir, string exeName)
    {
        if (!Directory.Exists(versionDir)) return null;

        var fullExeName = EnsureExeExtension(exeName);

        // 检查 bin/<exe>
        var binCandidate = Path.Combine(versionDir, "bin", fullExeName);
        if (File.Exists(binCandidate)) return binCandidate;

        // 检查根目录下的 <exe>
        var rootCandidate = Path.Combine(versionDir, fullExeName);
        if (File.Exists(rootCandidate)) return rootCandidate;

        return null;
    }
}
