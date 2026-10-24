using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// Volta JavaScript 工具管理器探测器。
/// 优先级: 70
/// </summary>
public class VoltaDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null)
    : BaseDetector(envGetter, rootDirOverride)
{
    public override string Name => "volta";
    public override int Order => 70;

    public override ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default)
    {
        if (!string.Equals(commandType, CommandTypes.NodeJs, StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var voltaHome = RootDirOverride;
        if (string.IsNullOrWhiteSpace(voltaHome))
        {
            voltaHome = EnvGetter("VOLTA_HOME");
            if (string.IsNullOrWhiteSpace(voltaHome))
            {
                var home = GetHomeDirectory();
                var candidate = Path.Combine(home, ".volta");
                if (Directory.Exists(candidate))
                {
                    voltaHome = candidate;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(voltaHome) || !Directory.Exists(voltaHome))
        {
            return ValueTask.FromResult<string?>(null);
        }

        var exeName = EnsureExeExtension("node");

        // 检查 bin/<exe>
        var binNode = Path.Combine(voltaHome, "bin", exeName);
        if (File.Exists(binNode))
        {
            return ValueTask.FromResult<string?>(binNode);
        }

        // 检查根目录下的 <exe>
        var rootNode = Path.Combine(voltaHome, exeName);
        if (File.Exists(rootNode))
        {
            return ValueTask.FromResult<string?>(rootNode);
        }

        return ValueTask.FromResult<string?>(null);
    }
}
