namespace TaskScheduler.Core.Models;

/// <summary>
/// 任务执行日志记录
/// </summary>
public class ExecutionLog
{
    /// <summary>日志 ID</summary>
    public int Id { get; set; }

    /// <summary>任务名称</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>任务分组</summary>
    public string JobGroup { get; set; } = string.Empty;

    /// <summary>开始时间</summary>
    public DateTime StartTime { get; set; }

    /// <summary>结束时间</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>执行耗时（毫秒）</summary>
    public long? DurationMs { get; set; }

    /// <summary>退出码</summary>
    public int? ExitCode { get; set; }

    /// <summary>执行状态</summary>
    public ExecutionStatus Status { get; set; }

    /// <summary>标准输出</summary>
    public string? Output { get; set; }

    /// <summary>错误输出</summary>
    public string? Error { get; set; }

    /// <summary>备注</summary>
    public string? Remark { get; set; }

    /// <summary>格式化的耗时字符串</summary>
    public string DurationDisplay
    {
        get
        {
            if (DurationMs == null) return "—";
            var ts = TimeSpan.FromMilliseconds(DurationMs.Value);
            if (ts.TotalMinutes >= 1)
                return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            if (ts.TotalSeconds >= 1)
                return $"{ts.TotalSeconds:F1}s";
            return $"{ts.Milliseconds}ms";
        }
    }

    /// <summary>
    /// 格式化为终端输出行列表（供 UI 与 CLI 导出/展示共用）
    /// </summary>
    public List<string> FormatTerminalLines()
    {
        var lines = new List<string>
        {
            $"任务: {JobName} ({JobGroup})",
            $"状态: {Status}",
            $"开始: {StartTime:yyyy-MM-dd HH:mm:ss}",
            $"耗时: {DurationDisplay}",
            $"退出码: {ExitCode?.ToString() ?? "—"}",
            ""
        };
        if (!string.IsNullOrEmpty(Output))
        {
            lines.Add("=== 标准输出 ===");
            lines.Add(Output);
        }

        if (!string.IsNullOrEmpty(Error))
        {
            lines.Add("=== 错误输出 ===");
            lines.Add(Error);
        }

        return lines;
    }
}
