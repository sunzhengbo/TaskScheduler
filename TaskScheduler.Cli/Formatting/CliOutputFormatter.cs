using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Cli.Formatting;

/// <summary>
/// 统一终端表格、状态徽章与 JSON 输出辅助器
/// </summary>
public static class CliOutputFormatter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void WriteJson<T>(T data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        Console.Out.WriteLine(json);
    }

    public static void PrintSuccess(string message, bool isJson = false)
    {
        if (isJson)
        {
            WriteJson(new { success = true, message });
            return;
        }
        AnsiConsole.MarkupLine($"[green]✔[/] {Markup.Escape(message)}");
    }

    public static void PrintError(string message, bool isJson = false)
    {
        if (isJson)
        {
            WriteJson(new { success = false, error = message });
            return;
        }
        AnsiConsole.MarkupLine($"[red]✖ {Markup.Escape(message)}[/]");
    }

    public static void PrintWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(message)}[/]");
    }

    public static void PrintInfo(string message)
    {
        AnsiConsole.MarkupLine($"[cyan]ℹ[/] {Markup.Escape(message)}");
    }

    public static string FormatTriggerStateMarkup(TriggerState state) => state switch
    {
        TriggerState.Normal => "[green]运行中 (Normal)[/]",
        TriggerState.Paused => "[yellow]已暂停 (Paused)[/]",
        TriggerState.Complete => "[blue]已完成 (Complete)[/]",
        TriggerState.Blocked => "[cyan]执行中 (Running)[/]",
        TriggerState.Error => "[red]错误 (Error)[/]",
        _ => "[grey]无 (None)[/]"
    };

    public static string FormatExecutionStatusMarkup(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Success => "[green]成功 (Success)[/]",
        ExecutionStatus.Failed => "[red]失败 (Failed)[/]",
        ExecutionStatus.Timeout => "[darkorange]超时 (Timeout)[/]",
        ExecutionStatus.Cancelled => "[yellow]已取消 (Cancelled)[/]",
        _ => $"[grey]{Markup.Escape(status.ToString())}[/]"
    };

    public static string FormatPriorityMarkup(TaskPriority priority) => priority switch
    {
        TaskPriority.High => "[red]高 (High)[/]",
        TaskPriority.Normal => "[blue]普通 (Normal)[/]",
        TaskPriority.Low => "[grey]低 (Low)[/]",
        _ => Markup.Escape(priority.ToString())
    };

    public static string FormatDateTime(DateTime? dt)
    {
        return dt.HasValue ? dt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
    }

    public static string FormatDateTimeOffset(DateTimeOffset? dto)
    {
        return dto.HasValue ? dto.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
    }

    public static string FormatCountdown(DateTimeOffset nextFireTime)
    {
        var local = nextFireTime.ToLocalTime();
        var diff = local - DateTimeOffset.Now;
        if (diff.TotalSeconds <= 0)
            return $"{local:HH:mm:ss} (即将触发)";
        if (diff.TotalMinutes < 1)
            return $"{(int)diff.TotalSeconds} 秒后 ({local:HH:mm:ss})";
        if (diff.TotalHours < 1)
            return $"{(int)diff.TotalMinutes} 分钟后 ({local:HH:mm})";
        if (diff.TotalDays < 1)
            return $"{(int)diff.TotalHours} 小时 {diff.Minutes} 分后 ({local:HH:mm})";
        return $"{local:MM-dd HH:mm} ({(int)diff.TotalDays} 天后)";
    }

    public static string FormatTimeAgo(DateTime startTime)
    {
        var localTime = startTime.Kind == DateTimeKind.Utc ? startTime.ToLocalTime() : startTime;
        var diff = DateTime.Now - localTime;
        if (diff.TotalSeconds < 60)
            return "刚刚";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours} 小时前";
        return $"{localTime:MM-dd HH:mm}";
    }
}
