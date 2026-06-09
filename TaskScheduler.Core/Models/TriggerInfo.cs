namespace TaskScheduler.Core.Models;

public enum TriggerType
{
    Simple,
    Cron
}

public enum TriggerState
{
    Normal,
    Paused,
    Complete,
    Blocked,
    Error,
    None
}

public class TriggerInfo
{
    public string? Name { get; set; }
    public string? Group { get; set; }
    public TriggerType Type { get; set; }
    public DateTimeOffset? NextFireTime { get; set; }
    public DateTimeOffset? PreviousFireTime { get; set; }
    public TriggerState State { get; set; }
    public int RepeatCount { get; set; }
    public TimeSpan? RepeatInterval { get; set; }
    public string? CronExpression { get; set; }
    public string? TimeZoneId { get; set; }

    /// <summary>触发器显示文本（供 UI 绑定使用）</summary>
    public string TriggerDisplay => Type switch
    {
        TriggerType.Cron when !string.IsNullOrWhiteSpace(CronExpression) => FormatCronExpression(CronExpression),
        TriggerType.Simple when RepeatInterval.HasValue
            => RepeatCount == -1
                ? $"每 {RepeatInterval.Value.TotalMinutes:0} 分钟"
                : $"每 {RepeatInterval.Value.TotalMinutes:0} 分钟 x {RepeatCount}",
        _ => Type.ToString()
    };

    /// <summary>
    /// 格式化 Cron 表达式：Quartz 使用 6/7 字段格式（含秒），
    /// 当秒字段为 "0" 时，隐藏秒字段以显示用户输入的 5 字段格式
    /// </summary>
    private static string FormatCronExpression(string cron)
    {
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6 && parts[0] == "0")
        {
            return string.Join(' ', parts.Skip(1));
        }
        return cron;
    }
}
