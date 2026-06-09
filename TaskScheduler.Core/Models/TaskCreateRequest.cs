namespace TaskScheduler.Core.Models;

public class TaskCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = "DEFAULT";
    public string? Description { get; set; }
    public Type JobType { get; set; } = null!;
    public IDictionary<string, object?>? JobData { get; set; }
    public TriggerType TriggerType { get; set; }
    public int RepeatCount { get; set; }
    public TimeSpan RepeatInterval { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";
}
