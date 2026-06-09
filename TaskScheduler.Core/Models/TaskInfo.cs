namespace TaskScheduler.Core.Models;

public class TaskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = "DEFAULT";
    public string? Description { get; set; }
    public string JobClassName { get; set; } = string.Empty;
    public bool IsDurable { get; set; }
    public bool RequestsRecovery { get; set; }
}
