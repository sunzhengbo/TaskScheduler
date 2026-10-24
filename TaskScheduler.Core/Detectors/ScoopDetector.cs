using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// Scoop 包管理工具探测器。
/// 优先级: 20
/// </summary>
public class ScoopDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "scoop";
    public override int Order => 20;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        var scoopDir = RootDirOverride;
        if (string.IsNullOrWhiteSpace(scoopDir))
        {
            scoopDir = EnvGetter("SCOOP");
            if (string.IsNullOrWhiteSpace(scoopDir))
            {
                var home = GetHomeDirectory();
                var defaultScoop = Path.Combine(home, "scoop");
                if (Directory.Exists(defaultScoop))
                {
                    scoopDir = defaultScoop;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(scoopDir) || !Directory.Exists(scoopDir))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var candidates = MapCommandTypeToAppAndExeCandidates(commandType);
        if (candidates.Length == 0)
        {
            return ValueTask.FromResult<string?>(null);
        }

        foreach (var (appName, exeBaseName) in candidates)
        {
            var fullExeName = EnsureExeExtension(exeBaseName);

            // 1. 优先检索 apps/<app>/current/<exe>
            var currentAppPath = Path.Combine(scoopDir, "apps", appName, "current", fullExeName);
            if (File.Exists(currentAppPath))
            {
                return ValueTask.FromResult<string?>(currentAppPath);
            }

            // 2. 检索 shims/<exe>
            var shimPath = Path.Combine(scoopDir, "shims", fullExeName);
            if (File.Exists(shimPath))
            {
                return ValueTask.FromResult<string?>(shimPath);
            }
        }

        return ValueTask.FromResult<string?>(null);
    }

    private static (string AppName, string ExeName)[] MapCommandTypeToAppAndExeCandidates(string commandType)
    {
        return commandType switch
        {
            CommandTypes.PowerShell => [("pwsh", "pwsh")],
            CommandTypes.Python => [("python", "python")],
            CommandTypes.NodeJs => [("nodejs", "node"), ("nodejs-lts", "node")],
            _ => []
        };
    }
}
