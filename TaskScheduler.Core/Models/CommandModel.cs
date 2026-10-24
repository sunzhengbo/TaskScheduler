using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskScheduler.Core.Models;

public partial class CommandModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _type = CommandTypes.PowerShell;
    private string _content = string.Empty;
    private string? _scriptMode;
    private string? _interpreterVersion;
    private string? _description;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set => SetField(ref _type, value);
    }

    public string Content
    {
        get => _content;
        set => SetField(ref _content, value);
    }

    /// <summary>
    /// 脚本执行模式：<see cref="ScriptModes.Inline"/>（代码内容）、<see cref="ScriptModes.File"/>（物理文件路径），或 null（旧版未指定时自动兼容探测）。
    /// </summary>
    public string? ScriptMode
    {
        get => _scriptMode;
        set => SetField(ref _scriptMode, value);
    }

    public string? InterpreterVersion
    {
        get => _interpreterVersion;
        set
        {
            if (SetField(ref _interpreterVersion, value))
            {
                OnPropertyChanged(nameof(InterpreterPath));
                OnPropertyChanged(nameof(InterpreterName));
            }
        }
    }

    /// <summary>
    /// 从解释器标识中提取纯净的版本名称（如 "pwsh (C:\path)" 提取 "pwsh"；若无路径分隔符则返回自身）。
    /// </summary>
    public string? InterpreterName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InterpreterVersion)) return null;
            var match = InterpreterSnapshotRegex().Match(InterpreterVersion.Trim());
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // 若自身不包含路径分隔符，则自身就是版本名称
            if (!InterpreterVersion.Contains('/') && !InterpreterVersion.Contains('\\'))

            {
                return InterpreterVersion.Trim();
            }

            return null;
        }
    }

    /// <summary>
    /// 从解释器版本字符串中提取路径（括号内的部分），若无括号且自身为有效路径则返回自身。
    /// </summary>
    public string? InterpreterPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InterpreterVersion)) return null;
            var match = InterpreterSnapshotRegex().Match(InterpreterVersion.Trim());
            if (match.Success)
                return match.Groups[2].Value.Trim();
            return InterpreterVersion.Trim();
        }
    }

    public string? Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

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
        catch (JsonException)
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
        catch (JsonException)
        {
            // 反序列化失败
        }

        return null;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    [GeneratedRegex(@"^(.+?)\s*\((.+)\)$")]
    private static partial Regex InterpreterSnapshotRegex();
}
