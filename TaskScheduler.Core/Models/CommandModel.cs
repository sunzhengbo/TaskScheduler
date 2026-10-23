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

    public string? InterpreterVersion
    {
        get => _interpreterVersion;
        set
        {
            if (SetField(ref _interpreterVersion, value))
            {
                OnPropertyChanged(nameof(InterpreterPath));
            }
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
            var match = InterpreterPathRegex().Match(InterpreterVersion);
            if (match.Success)
                return match.Groups[1].Value;
            return InterpreterVersion;
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

    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex InterpreterPathRegex();
}
