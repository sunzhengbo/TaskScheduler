namespace TaskScheduler.Core.Models;

/// <summary>
/// Describes whether a formatter is supported and available in the current environment.
/// </summary>
public sealed record ScriptFormatterCapability(
    string Language,
    bool Supported,
    bool Available,
    string? Tool,
    string? Version,
    string? Reason);
