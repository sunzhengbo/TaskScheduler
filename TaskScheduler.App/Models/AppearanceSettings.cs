using System.Text.Json;

namespace TaskScheduler.App.Models;

public class AppearanceSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ThemeMode { get; set; } = "System";
    public bool CompactMode { get; set; }

    public static AppearanceSettings FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<AppearanceSettings>(json, JsonOptions) ?? new(); }
        catch { return new(); }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
