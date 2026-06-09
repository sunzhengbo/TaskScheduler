namespace TaskScheduler.Core.Models;

/// <summary>
/// 任务执行状态枚举
/// </summary>
public enum ExecutionStatus
{
    /// <summary>成功</summary>
    Success,

    /// <summary>失败</summary>
    Failed,

    /// <summary>超时</summary>
    Timeout,

    /// <summary>已取消</summary>
    Cancelled
}
