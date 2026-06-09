using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 工具配置服务
/// </summary>
public interface IToolConfigService
{
    /// <summary>获取所有工具配置</summary>
    Task<IReadOnlyList<ToolConfig>> GetAllToolsAsync(CancellationToken ct = default);

    /// <summary>按类型获取工具配置</summary>
    Task<IReadOnlyList<ToolConfig>> GetToolsByTypeAsync(string toolType, CancellationToken ct = default);

    /// <summary>添加工具配置</summary>
    Task<ToolConfig> AddToolAsync(ToolConfig tool, CancellationToken ct = default);

    /// <summary>更新工具配置</summary>
    Task UpdateToolAsync(ToolConfig tool, CancellationToken ct = default);

    /// <summary>删除工具配置</summary>
    Task DeleteToolAsync(int id, CancellationToken ct = default);

    /// <summary>设置默认版本</summary>
    Task SetDefaultVersionAsync(int toolId, CancellationToken ct = default);

    /// <summary>获取指定类型的默认工具</summary>
    Task<ToolConfig?> GetDefaultToolAsync(string toolType, CancellationToken ct = default);
}
