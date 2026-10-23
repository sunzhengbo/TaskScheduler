using System.Text.Json.Serialization;

namespace TaskScheduler.Core.Models;

/// <summary>
/// 命令导出模型。
/// </summary>
public class CommandExportModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("interpreterVersion")]
    public string? InterpreterVersion { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
