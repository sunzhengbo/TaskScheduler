using System.Text.Json;

namespace TaskScheduler.Core.Models;

/// <summary>
/// Paths and execution limits for optional external script formatters.
/// </summary>
public sealed class ScriptFormatterSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 格式化设置在应用设置存储中的键。
    /// </summary>
    public const string StorageKey = "script_formatter_settings";

    public int TimeoutSeconds { get; set; } = ScriptFormatterDefaults.DefaultTimeoutSeconds;
    public string? BlackExecutablePath { get; set; }
    public string? PsscriptAnalyzerModulePath { get; set; }
    public string? ShfmtExecutablePath { get; set; }
    public string? PrettierPackagePath { get; set; }

    public static ScriptFormatterSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ScriptFormatterSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ScriptFormatterSettings>(json, JsonOptions)
                ?? new ScriptFormatterSettings();
            if (!IsValidTimeout(settings.TimeoutSeconds))
            {
                settings.TimeoutSeconds = ScriptFormatterDefaults.DefaultTimeoutSeconds;
            }

            settings.NormalizePaths();
            return settings;
        }
        catch (JsonException)
        {
            return new ScriptFormatterSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// 把四个路径字段中的空白值统一折叠为 <see langword="null"/> 并去掉首尾空白。
    /// 空串与未配置在逻辑上等价（所有消费点都用 <c>IsNullOrWhiteSpace</c> 判断），
    /// 但存储与 <c>show --json</c> 输出不应出现两种表示。
    /// </summary>
    public void NormalizePaths()
    {
        BlackExecutablePath = NormalizePath(BlackExecutablePath);
        PsscriptAnalyzerModulePath = NormalizePath(PsscriptAnalyzerModulePath);
        ShfmtExecutablePath = NormalizePath(ShfmtExecutablePath);
        PrettierPackagePath = NormalizePath(PrettierPackagePath);
    }

    public void Validate()
    {
        if (!IsValidTimeout(TimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds),
                $"格式化超时必须在 {ScriptFormatterDefaults.MinTimeoutSeconds} 到 {ScriptFormatterDefaults.MaxTimeoutSeconds} 秒之间。");
        }
    }

    /// <summary>超时秒数是否落在允许区间内。</summary>
    public static bool IsValidTimeout(int timeoutSeconds) =>
        timeoutSeconds is >= ScriptFormatterDefaults.MinTimeoutSeconds and <= ScriptFormatterDefaults.MaxTimeoutSeconds;

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}
