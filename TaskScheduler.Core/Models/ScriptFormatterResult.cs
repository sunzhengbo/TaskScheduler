using System.Text.Json.Serialization;

namespace TaskScheduler.Core.Models;

/// <summary>
/// Result of formatting a script buffer.
/// </summary>
public sealed record ScriptFormatterResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("output")] string? Output,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("tool")] string? Tool,
    [property: JsonPropertyName("version")] string? Version);
