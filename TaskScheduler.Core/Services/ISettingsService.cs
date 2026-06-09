namespace TaskScheduler.Core.Services;

/// <summary>
/// 应用设置服务
/// </summary>
public interface ISettingsService
{
    /// <summary>获取设置值</summary>
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);

    /// <summary>同步获取设置值（适用于启动阶段等无法使用 async 的场景）</summary>
    string? GetValue(string key);

    /// <summary>设置值</summary>
    Task SetValueAsync(string key, string value, CancellationToken ct = default);

    /// <summary>获取所有设置</summary>
    Task<IDictionary<string, string>> GetAllAsync(CancellationToken ct = default);

    /// <summary>删除设置</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
