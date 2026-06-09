namespace TaskScheduler.Core.Models;

/// <summary>
/// 应用设置键值对
/// </summary>
public class AppSetting
{
    /// <summary>设置键</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>设置值</summary>
    public string? Value { get; set; }

    /// <summary>更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
