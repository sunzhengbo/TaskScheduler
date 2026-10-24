namespace TaskScheduler.Core.Detectors;

/// <summary>
/// 版本管理探测器基础抽象类，支持环境变量读取委托与根目录注入以满足单元测试隔离。
/// </summary>
public abstract class BaseDetector(Func<string, string?>? envGetter = null, string? rootDirOverride = null) : IVersionManagerDetector
{
    protected readonly Func<string, string?> EnvGetter = envGetter ?? GetEnvironmentVariableWithFallback;
    protected readonly string? RootDirOverride = rootDirOverride;

    public abstract string Name { get; }
    public virtual int Order => 100;

    public abstract ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default);

    /// <summary>
    /// 获取当前用户主目录路径。
    /// </summary>
    protected static string GetHomeDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// 确保可执行文件名在 Windows 平台具有 .exe 后缀。
    /// </summary>
    protected static string EnsureExeExtension(string fileName)
    {
        if (OperatingSystem.IsWindows() && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return fileName + ".exe";
        }
        return fileName;
    }

    /// <summary>
    /// 获取环境变量值（依次尝试 Process -> User -> Machine 三个级别）。
    /// </summary>
    public static string? GetEnvironmentVariableWithFallback(string name)
    {
        var val = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(val)) return val;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                val = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(val)) return val;

                val = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
            catch
            {
                // 忽略受限权限环境异常
            }
        }

        return null;
    }
}
