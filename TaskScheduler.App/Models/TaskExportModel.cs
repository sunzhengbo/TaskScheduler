using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

using TaskScheduler.Core.Models;

namespace TaskScheduler.App.Models;

/// <summary>
/// 任务导出/导入模型，包含任务基本信息、触发器配置和单个命令。
/// 用于任务的整体导出（JSON 文件）和剪贴板复制/粘贴。
/// </summary>
public class TaskExportModel
{
    public const int CurrentVersion = 2;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = "DEFAULT";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("triggerType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TriggerType TriggerType { get; set; }

    [JsonPropertyName("repeatCount")]
    public int RepeatCount { get; set; }

    [JsonPropertyName("repeatInterval")]
    public string RepeatInterval { get; set; } = "00:05:00";

    [JsonPropertyName("cronExpression")]
    public string CronExpression { get; set; } = string.Empty;

    [JsonPropertyName("useBootTime")]
    public bool UseBootTime { get; set; }

    [JsonPropertyName("command")]
    public CommandExportModel Command { get; set; } = new();

    /// <summary>
    /// 从 ScheduledTaskDetail 构建导出模型。
    /// </summary>
    public static TaskExportModel FromTask(ScheduledTaskDetail task, string commandJson)
    {
        var trigger = task.Triggers.Count > 0 ? task.Triggers[0] : null;
        var command = new CommandExportModel();

        var cmd = CommandModel.DeserializeFromJson(commandJson);
        if (cmd != null)
        {
            command = new CommandExportModel
            {
                Name = cmd.Name,
                Type = cmd.Type,
                Content = cmd.Content,
                InterpreterVersion = cmd.InterpreterVersion,
                Description = cmd.Description
            };
        }

        return new TaskExportModel
        {
            Name = task.Name,
            Group = task.Group,
            Description = task.Description,
            TriggerType = trigger?.Type ?? TriggerType.Simple,
            RepeatCount = trigger?.RepeatCount ?? 0,
            RepeatInterval = trigger?.RepeatInterval?.ToString() ?? "00:05:00",
            CronExpression = trigger?.CronExpression ?? string.Empty,
            UseBootTime = task.UseBootTime,
            Command = command
        };
    }

    /// <summary>
    /// 序列化为格式化的 JSON 字符串。
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 从 JSON 字符串反序列化导出模型。
    /// </summary>
    public static TaskExportModel? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TaskExportModel>(json);
        }
        catch
        {
            return null;
        }
    }
}

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
