using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// Astral uv Python 管理工具探测器。
/// 优先级: 30
/// </summary>
public class UvDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "uv";
    public override int Order => 30;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        if (!string.Equals(commandType, CommandTypes.Python, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var uvPythonDir = RootDirOverride;
        if (string.IsNullOrWhiteSpace(uvPythonDir))
        {
            uvPythonDir = EnvGetter("UV_PYTHON_INSTALL_DIR");
            if (string.IsNullOrWhiteSpace(uvPythonDir))
            {
                var localAppData = EnvGetter("LOCALAPPDATA");
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    var winCandidate = Path.Combine(localAppData, "uv", "python");
                    if (Directory.Exists(winCandidate))
                    {
                        uvPythonDir = winCandidate;
                    }
                }

                if (string.IsNullOrWhiteSpace(uvPythonDir))
                {
                    var home = GetHomeDirectory();
                    var posixCandidate = Path.Combine(home, ".local", "share", "uv", "python");
                    if (Directory.Exists(posixCandidate))
                    {
                        uvPythonDir = posixCandidate;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(uvPythonDir) || !Directory.Exists(uvPythonDir))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var exeName = EnsureExeExtension("python");

        // 倒序枚举 uv 安装的各个 Python 版本目录（如 cpython-3.12.7-...，优先语义化高版本）
        try
        {
            var versionDirs = Directory.GetDirectories(uvPythonDir);
            foreach (var dir in versionDirs.OrderByDescendingVersion())
            {
                // 检查根目录下的 python.exe (Windows 结构)
                var rootExe = Path.Combine(dir, exeName);
                if (File.Exists(rootExe)) return ValueTask.FromResult<string?>(rootExe);

                // 检查 bin/python (类 Unix / 虚拟环境结构)
                var binExe = Path.Combine(dir, "bin", exeName);
                if (File.Exists(binExe)) return ValueTask.FromResult<string?>(binExe);
            }
        }
        catch
        {
            // 忽略文件枚举异常
        }

        return ValueTask.FromResult<string?>(null);
    }
}
