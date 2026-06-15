using System.Text.Json;

namespace TaskScheduler.App.Models;

public class EngineSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int LogRetentionDays { get; set; } = 30;
    public int MaxThreads { get; set; } = 10;
    public string DatabaseType { get; set; } = "SQLite";
    public bool StartupEnabled { get; set; }
    public bool StartupMinimize { get; set; }

    /// <summary>启动时是否重新计算所有 SimpleTrigger 的起始时间</summary>
    public bool RescheduleOnStartup { get; set; } = true;

    public static EngineSettings FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<EngineSettings>(json, JsonOptions) ?? new(); }
        catch { return new(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
