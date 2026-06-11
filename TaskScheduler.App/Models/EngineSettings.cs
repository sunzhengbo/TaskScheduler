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

    public static EngineSettings FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<EngineSettings>(json, JsonOptions) ?? new(); }
        catch { return new(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
