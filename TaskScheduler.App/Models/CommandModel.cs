using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;
using JetBrains.Annotations;

namespace TaskScheduler.App.Models;

public static class CommandTypes
{
    public const string Cmd = "命令提示符 (cmd)";
    public const string PowerShell = "PowerShell";
    public const string Python = "Python 脚本";
    public const string Shell = "Shell 脚本";
    public const string NodeJs = "Node.js 脚本";
}

public partial class CommandModel : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    [UsedImplicitly]
    private string _name = string.Empty;

    [ObservableProperty]
    [UsedImplicitly]
    private string _type = CommandTypes.PowerShell;

    [ObservableProperty]
    [UsedImplicitly]
    private string _content = string.Empty;

    [ObservableProperty]
    [UsedImplicitly]
    private string? _interpreterVersion;

    /// <summary>
    /// 当解释器版本变更时，通知路径属性更新。
    /// </summary>
    partial void OnInterpreterVersionChanged(string? value)
    {
        OnPropertyChanged(nameof(InterpreterPath));
    }

    /// <summary>
    /// 从解释器版本字符串中提取路径（括号内的部分）。
    /// </summary>
    public string? InterpreterPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InterpreterVersion)) return null;
            var match = Regex.Match(InterpreterVersion, @"\(([^)]+)\)");
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    [ObservableProperty]
    [UsedImplicitly]
    private string? _description;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 从 JSON 字符串反序列化命令，兼容新的单命令格式和旧的数组格式。
    /// </summary>
    public static CommandModel? DeserializeFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        // 优先尝试单命令格式
        try
        {
            var cmd = JsonSerializer.Deserialize<CommandModel>(json);
            if (cmd != null && !string.IsNullOrEmpty(cmd.Content))
                return cmd;
        }
        catch
        {
            // 单命令解析失败，尝试数组格式
        }

        // 兼容旧的数组格式
        try
        {
            var commands = JsonSerializer.Deserialize<List<CommandModel>>(json);
            if (commands != null && commands.Count > 0)
                return commands[0];
        }
        catch
        {
            // 反序列化失败
        }

        return null;
    }
}
