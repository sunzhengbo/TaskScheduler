namespace TaskScheduler.Core.Detectors;

/// <summary>
/// 解释器环境探测聚合服务契约，负责系统 PATH 检索与多版本环境管理器轮询。
/// </summary>
public interface IInterpreterDetectionService
{
    /// <summary>
    /// 当前聚合服务所加载的多版本管理器探测器名称列表。
    /// </summary>
    IReadOnlyList<string> DetectorNames => Array.Empty<string>();

    /// <summary>
    /// 仅在操作系统环境变量 PATH 中实时查找可用解释器（含两段式候选名）。
    /// </summary>
    /// <param name="commandType">命令语言类型（已归一化，如 PowerShell, Python, NodeJs 等）</param>
    /// <param name="customCandidate">用户指定的自定义候选可执行文件名或版本名称</param>
    /// <returns>若在 PATH 中检索到有效文件则返回绝对物理路径；否则返回 null</returns>
    string? SearchInPath(string commandType, string? customCandidate = null);

    /// <summary>
    /// 完整进行第 2 级 (系统 PATH 实时检索) 与第 3 级 (多版本管理探测器轮询，带 5 分钟 TTL 缓存) 的综合探测。
    /// </summary>
    /// <param name="commandType">命令语言类型</param>
    /// <param name="customCandidate">用户指定的自定义候选名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>若探测到有效解释器则返回物理绝对路径；否则返回 null</returns>
    ValueTask<string?> DetectAsync(string commandType, string? customCandidate = null, CancellationToken ct = default);
}
