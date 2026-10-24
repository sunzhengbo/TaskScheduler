using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// NVM (Node Version Manager for Windows / Unix) 探测器。
/// 优先级: 50
/// </summary>
public class NvmDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "nvm";
    public override int Order => 50;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        if (!string.Equals(commandType, CommandTypes.NodeJs, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var exeName = EnsureExeExtension("node");

        // 1. 优先检查当前激活的 NVM_SYMLINK 软链接 (nvm-windows 常用)
        var nvmSymlink = EnvGetter("NVM_SYMLINK");
        if (!string.IsNullOrWhiteSpace(nvmSymlink))
        {
            var nodePath = Path.Combine(nvmSymlink, exeName);
            if (File.Exists(nodePath))
            {
                return ValueTask.FromResult<string?>(nodePath);
            }
        }

        // 2. 检查 NVM_HOME 或 APPDATA/nvm
        var nvmHome = RootDirOverride;
        if (string.IsNullOrWhiteSpace(nvmHome))
        {
            nvmHome = EnvGetter("NVM_HOME");
            if (string.IsNullOrWhiteSpace(nvmHome))
            {
                var appData = EnvGetter("APPDATA");
                if (!string.IsNullOrWhiteSpace(appData))
                {
                    var winCandidate = Path.Combine(appData, "nvm");
                    if (Directory.Exists(winCandidate))
                    {
                        nvmHome = winCandidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(nvmHome) || !Directory.Exists(nvmHome))
        {
            return ValueTask.FromResult<string?>(null);
        }

        // 倒序枚举版本目录（如 v20.10.0，优先语义化高版本）
        try
        {
            var versionDirs = Directory.GetDirectories(nvmHome);
            foreach (var dir in versionDirs.OrderByDescendingVersion())
            {
                var nodeExe = Path.Combine(dir, exeName);
                if (File.Exists(nodeExe))
                {
                    return ValueTask.FromResult<string?>(nodeExe);
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
