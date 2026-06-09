namespace TaskScheduler.Core.Models;

/// <summary>
/// 单个任务的统计信息
/// </summary>
public class TaskStatistics
{
    /// <summary>任务名称</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>任务分组</summary>
    public string JobGroup { get; set; } = string.Empty;

    /// <summary>累计执行次数</summary>
    public int TotalExecutions { get; set; }

    /// <summary>成功次数</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败次数</summary>
    public int FailCount { get; set; }

    /// <summary>成功率（0~100）</summary>
    public double SuccessRate => TotalExecutions == 0 ? 0 : (double)SuccessCount / TotalExecutions * 100;

    /// <summary>平均耗时（毫秒）</summary>
    public long AverageDurationMs { get; set; }

    /// <summary>上次执行时间</summary>
    public DateTime? LastExecutionTime { get; set; }

    /// <summary>上次执行结果</summary>
    public ExecutionStatus? LastStatus { get; set; }

    /// <summary>上次执行耗时（毫秒）</summary>
    public long? LastDurationMs { get; set; }

    /// <summary>连续失败次数</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>成功率显示文本</summary>
    public string SuccessRateDisplay =>
        $"{SuccessRate:F1}%（{SuccessCount}/{TotalExecutions}）";

    /// <summary>平均耗时显示文本</summary>
    public string AverageDurationDisplay
    {
        get
        {
            if (AverageDurationMs == 0) return "—";
            var ts = TimeSpan.FromMilliseconds(AverageDurationMs);
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            if (ts.TotalSeconds >= 1)
                return $"{ts.TotalSeconds:F1}s";
            return $"{ts.Milliseconds}ms";
        }
    }
}

/// <summary>
/// 仪表盘统计概览
/// </summary>
public class DashboardStats
{
    /// <summary>全部任务数</summary>
    public int TotalCount { get; set; }

    /// <summary>运行中的任务数</summary>
    public int RunningCount { get; set; }

    /// <summary>已暂停的任务数</summary>
    public int PausedCount { get; set; }

    /// <summary>失败的任务数</summary>
    public int FailedCount { get; set; }
}
