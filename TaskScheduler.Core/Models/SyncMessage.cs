namespace TaskScheduler.Core.Models;

/// <summary>
/// 跨进程同步消息载体
/// </summary>
public sealed class SyncMessage
{
    public SyncEventType Type { get; set; }

    public string? JobName { get; set; }

    public string? JobGroup { get; set; }

    public string? Key { get; set; }
}
