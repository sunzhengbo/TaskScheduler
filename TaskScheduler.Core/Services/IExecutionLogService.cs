using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 执行日志服务
/// </summary>
public interface IExecutionLogService
{
    /// <summary>记录一次执行</summary>
    Task RecordExecutionAsync(ExecutionLog log, CancellationToken ct = default);

    /// <summary>根据 ID 查询单条日志</summary>
    Task<ExecutionLog?> GetLogByIdAsync(int id, CancellationToken ct = default);

    /// <summary>分页查询日志</summary>
    Task<IReadOnlyList<ExecutionLog>> GetLogsAsync(string? jobName, string? jobGroup,
        string? status, int page, int pageSize, CancellationToken ct = default, string? searchKeyword = null);

    /// <summary>查询总记录数（用于分页）</summary>
    Task<int> GetLogCountAsync(string? jobName, string? jobGroup,
        string? status, CancellationToken ct = default, string? searchKeyword = null);

    /// <summary>获取单个任务的统计信息</summary>
    Task<TaskStatistics> GetStatisticsAsync(string jobName, string jobGroup, CancellationToken ct = default);

    /// <summary>获取仪表盘统计概览</summary>
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default);

    /// <summary>获取最近 N 条日志</summary>
    Task<IReadOnlyList<ExecutionLog>> GetRecentLogsAsync(int count, CancellationToken ct = default);

    /// <summary>获取所有任务各自最近一次的执行记录字典（Key 格式为 "jobGroup.jobName"）</summary>
    Task<IReadOnlyDictionary<string, ExecutionLog>> GetLatestLogsForAllTasksAsync(CancellationToken ct = default);

    /// <summary>获取指定任务最近一次的执行记录</summary>
    Task<ExecutionLog?> GetLatestLogAsync(string jobName, string jobGroup, CancellationToken ct = default);
}
