using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// Fnm (Fast Node Manager) 探测器。
/// 优先级: 60
/// </summary>
public class FnmDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "fnm";
    public override int Order => 60;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        if (!string.Equals(commandType, CommandTypes.NodeJs, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var fnmDir = RootDirOverride;
        if (string.IsNullOrWhiteSpace(fnmDir))
        {
            fnmDir = EnvGetter("FNM_DIR");
            if (string.IsNullOrWhiteSpace(fnmDir))
            {
                var localAppData = EnvGetter("LOCALAPPDATA");
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    var winCandidate = Path.Combine(localAppData, "fnm_multishells");
                    if (Directory.Exists(winCandidate))
                    {
                        fnmDir = winCandidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(fnmDir) || !Directory.Exists(fnmDir))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var exeName = EnsureExeExtension("node");

        // 1. 检查 current 软链接目录
        var currentDir = Path.Combine(fnmDir, "current");
        var nodeInCurrent = Path.Combine(currentDir, exeName);
        if (File.Exists(nodeInCurrent))
        {
            return ValueTask.FromResult<string?>(nodeInCurrent);
        }

        // 2. 检查根目录下的 node 可执行文件
        var directNode = Path.Combine(fnmDir, exeName);
        if (File.Exists(directNode))
        {
            return ValueTask.FromResult<string?>(directNode);
        }

        // 3. 检查子目录（支持 fnm_multishells 动态会话哈希目录与安装版本目录）
        try
        {
            var subDirs = Directory.EnumerateDirectories(fnmDir)
                .OrderByDescending(Directory.GetLastWriteTimeUtc);
            foreach (var subDir in subDirs)
            {
                var subExe = Path.Combine(subDir, exeName);
                if (File.Exists(subExe))
                {
                    return ValueTask.FromResult<string?>(subExe);
                }

                var binExe = Path.Combine(subDir, "bin", exeName);
                if (File.Exists(binExe))
                {
                    return ValueTask.FromResult<string?>(binExe);
                }
            }
        }
        catch
        {
            // 忽略文件枚举权限异常
        }

        return ValueTask.FromResult<string?>(null);
    }
}
