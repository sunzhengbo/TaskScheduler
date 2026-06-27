namespace TaskScheduler.Core.Models;

/// <summary>
/// 任务优先级
/// </summary>
public enum TaskPriority
{
    /// <summary>低</summary>
    Low,

    /// <summary>普通</summary>
    Normal,

    /// <summary>高</summary>
    High
}

public class ScheduledTaskDetail : TaskInfo
{
    private string _commandJson = "{}";

    public List<TriggerInfo> Triggers { get; set; } = new();

    /// <summary>第一个触发器（可能为 null）</summary>
    public TriggerInfo? FirstTrigger => Triggers.Count > 0 ? Triggers[0] : null;

    /// <summary>任务优先级</summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    /// <summary>创建时间</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>最近编辑时间</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 任务关联的命令（JSON 序列化存储于 JobData）
    /// 默认值 "{}" 表示空命令
    /// </summary>
    public string CommandJson
    {
        get => _commandJson;
        set => _commandJson = value;
    }

    /// <summary>是否为开机启动</summary>
    public bool UseBootTime { get; set; }

    /// <summary>行是否选中（用于多选）</summary>
    public bool IsSelected { get; set; }

    /// <summary>任务是否启用（触发器状态为 Normal 即为启用）</summary>
    public bool IsEnabled => FirstTrigger?.State == TriggerState.Normal;
}
