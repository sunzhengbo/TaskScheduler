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
        if (string.IsNullOrEmpty(json)) return new AppearanceSettings();
        try
        {
            return JsonSerializer.Deserialize<AppearanceSettings>(json, JsonOptions) ?? new AppearanceSettings();
        }
        catch
        {
            return new AppearanceSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
