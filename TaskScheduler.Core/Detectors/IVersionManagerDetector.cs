namespace TaskScheduler.Core.Detectors;

/// <summary>
/// 多版本环境管理工具（如 mise, scoop, uv, asdf 等）探测器契约。
/// </summary>
public interface IVersionManagerDetector
{
    /// <summary>
    /// 管理工具名称（如 "mise", "scoop", "uv", "asdf", "nvm", "fnm", "volta"）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 优先级排序值（越小越优先执行，默认 100）。
    /// </summary>
    int Order => 100;

    /// <summary>
    /// 针对指定的命令语言类型，探测是否存在本机安装的有效可执行文件绝对物理路径。
    /// </summary>
    /// <param name="commandType">命令语言类型（已归一化，如 CommandTypes.PowerShell, CommandTypes.Python, CommandTypes.NodeJs 等）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>若探测到存在的文件则返回绝对物理路径；否则返回 null</returns>
    ValueTask<string?> DetectAsync(string commandType, CancellationToken ct = default);
}
