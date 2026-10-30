using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// Formats inline script text and reports formatter availability for UI and CLI callers.
/// </summary>
public interface IScriptFormatterService
{
    /// <summary>Loads formatter configuration, returning defaults when no valid value is stored.</summary>
    Task<ScriptFormatterSettings> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>Validates and persists formatter configuration.</summary>
    Task SaveSettingsAsync(ScriptFormatterSettings settings, CancellationToken ct = default);

    /// <summary>Checks formatter support and availability for a command language.</summary>
    Task<ScriptFormatterCapability> GetCapabilityAsync(
        string commandType,
        string? interpreterPath = null,
        CancellationToken ct = default);

    /// <summary>Formats an in-memory script without modifying any file.</summary>
    Task<ScriptFormatterResult> FormatAsync(
        string commandType,
        string source,
        string? interpreterPath = null,
        CancellationToken ct = default);
}
