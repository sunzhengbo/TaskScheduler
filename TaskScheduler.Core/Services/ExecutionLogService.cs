using Microsoft.Data.Sqlite;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 执行日志服务实现（基于 SQLite）
/// </summary>
public class ExecutionLogService : IExecutionLogService
{
    private readonly DatabaseProvider _db;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ExecutionLogService(DatabaseProvider db)
    {
        _db = db;
    }

    public async Task RecordExecutionAsync(ExecutionLog log, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            await conn.OpenAsync(ct);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO execution_logs (job_name, job_group, start_time, end_time, duration_ms, exit_code, status, output, error, remark)
                VALUES (@name, @group, @start, @end, @dur, @exit, @status, @output, @error, @remark)";
            cmd.Parameters.AddWithValue("@name", log.JobName);
            cmd.Parameters.AddWithValue("@group", log.JobGroup);
            cmd.Parameters.AddWithValue("@start", log.StartTime);
            cmd.Parameters.AddWithValue("@end", (object?)log.EndTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dur", (object?)log.DurationMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@exit", (object?)log.ExitCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@status", log.Status.ToString());
            cmd.Parameters.AddWithValue("@output", (object?)log.Output ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@error", (object?)log.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@remark", (object?)log.Remark ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<ExecutionLog>> GetLogsAsync(string? jobName, string? jobGroup,
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();

        var where = BuildWhereClause(cmd, jobName, jobGroup, status);
        cmd.CommandText = $@"
            SELECT id, job_name, job_group, start_time, end_time, duration_ms, exit_code, status, output, error, remark
            FROM execution_logs {where}
            ORDER BY start_time DESC
            LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", pageSize);
        cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

        return await ReadLogsAsync(cmd, ct);
    }

    public async Task<int> GetLogCountAsync(string? jobName, string? jobGroup,
        string? status, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();

        var where = BuildWhereClause(cmd, jobName, jobGroup, status);
        cmd.CommandText = $"SELECT COUNT(*) FROM execution_logs {where}";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<TaskStatistics> GetStatisticsAsync(string jobName, string jobGroup, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);

        var stats = new TaskStatistics { JobName = jobName, JobGroup = jobGroup };

        // 累计统计
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*) AS total,
                       SUM(CASE WHEN status='Success' THEN 1 ELSE 0 END) AS success,
                       SUM(CASE WHEN status='Failed' THEN 1 ELSE 0 END) AS fail,
                       AVG(CASE WHEN duration_ms IS NOT NULL THEN duration_ms END) AS avg_dur
                FROM execution_logs
                WHERE job_name=@name AND job_group=@group";
            cmd.Parameters.AddWithValue("@name", jobName);
            cmd.Parameters.AddWithValue("@group", jobGroup);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                stats.TotalExecutions = reader.GetInt32(0);
                stats.SuccessCount = reader.GetInt32(1);
                stats.FailCount = reader.GetInt32(2);
                stats.AverageDurationMs = reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetDouble(3));
            }
        }

        // 最近一次执行
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT start_time, status, duration_ms
                FROM execution_logs
                WHERE job_name=@name AND job_group=@group
                ORDER BY start_time DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@name", jobName);
            cmd.Parameters.AddWithValue("@group", jobGroup);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                stats.LastExecutionTime = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc).ToLocalTime();
                stats.LastStatus = Enum.Parse<ExecutionStatus>(reader.GetString(1));
                stats.LastDurationMs = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            }
        }

        // 连续失败次数
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT status FROM execution_logs
                WHERE job_name=@name AND job_group=@group
                ORDER BY start_time DESC";
            cmd.Parameters.AddWithValue("@name", jobName);
            cmd.Parameters.AddWithValue("@group", jobGroup);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            var consecutive = 0;
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetString(0) == "Failed")
                    consecutive++;
                else
                    break;
            }
            stats.ConsecutiveFailures = consecutive;
        }

        return stats;
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        // DashboardStats 主要依赖 Quartz 调度器的任务状态
        // 此方法返回基于日志的统计；完整统计在 ViewModel 层合并
        var stats = new DashboardStats();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                COUNT(DISTINCT job_name || '.' || job_group) AS total_jobs,
                COUNT(DISTINCT CASE WHEN status='Failed' AND start_time > datetime('now', '-1 day')
                    THEN job_name || '.' || job_group END) AS failed_jobs
            FROM execution_logs";

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            stats.TotalCount = reader.GetInt32(0);
            stats.FailedCount = reader.GetInt32(1);
        }

        return stats;
    }

    public async Task<IReadOnlyList<ExecutionLog>> GetRecentLogsAsync(int count, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, job_name, job_group, start_time, end_time, duration_ms, exit_code, status, output, error, remark
            FROM execution_logs
            ORDER BY start_time DESC
            LIMIT @count";
        cmd.Parameters.AddWithValue("@count", count);
        return await ReadLogsAsync(cmd, ct);
    }

    #region Helpers

    private static string BuildWhereClause(SqliteCommand cmd, string? jobName, string? jobGroup, string? status)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(jobName))
        {
            clauses.Add("job_name = @name");
            cmd.Parameters.AddWithValue("@name", jobName);
        }
        if (!string.IsNullOrWhiteSpace(jobGroup))
        {
            clauses.Add("job_group = @group");
            cmd.Parameters.AddWithValue("@group", jobGroup);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            clauses.Add("status = @status");
            cmd.Parameters.AddWithValue("@status", status);
        }
        return clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;
    }

    private static async Task<IReadOnlyList<ExecutionLog>> ReadLogsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var logs = new List<ExecutionLog>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            logs.Add(new ExecutionLog
            {
                Id = reader.GetInt32(0),
                JobName = reader.GetString(1),
                JobGroup = reader.GetString(2),
                StartTime = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc).ToLocalTime(),
                EndTime = reader.IsDBNull(4) ? null : DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc).ToLocalTime(),
                DurationMs = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                ExitCode = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                Status = Enum.Parse<ExecutionStatus>(reader.GetString(7)),
                Output = reader.IsDBNull(8) ? null : reader.GetString(8),
                Error = reader.IsDBNull(9) ? null : reader.GetString(9),
                Remark = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }
        return logs;
    }

    #endregion
}
