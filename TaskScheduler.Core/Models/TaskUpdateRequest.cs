namespace TaskScheduler.Core.Models;

public class TaskUpdateRequest
{
    public string? Description { get; set; }

    public IDictionary<string, object?>? JobData { get; set; }
}

public class TriggerUpdateRequest
{
    public int? RepeatCount { get; set; }
    public TimeSpan? RepeatInterval { get; set; }
    public string? CronExpression { get; set; }
    public string? TimeZoneId { get; set; }
}
