using Microsoft.Data.Sqlite;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 工具配置服务实现（基于 SQLite）
/// </summary>
public class ToolConfigService : IToolConfigService
{
    private readonly DatabaseProvider _db;

    public ToolConfigService(DatabaseProvider db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ToolConfig>> GetAllToolsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
            FROM tool_configs
            ORDER BY tool_type, version_name";
        return await ReadToolsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ToolConfig>> GetToolsByTypeAsync(string toolType, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
            FROM tool_configs
            WHERE tool_type = @type
            ORDER BY version_name";
        cmd.Parameters.AddWithValue("@type", toolType);
        return await ReadToolsAsync(cmd, ct);
    }

    public async Task<ToolConfig> AddToolAsync(ToolConfig tool, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tool_configs (tool_type, version_name, executable_path, is_default, env_variables)
            VALUES (@type, @version, @path, @isDefault, @env);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@type", tool.ToolType);
        cmd.Parameters.AddWithValue("@version", tool.VersionName);
        cmd.Parameters.AddWithValue("@path", tool.ExecutablePath);
        cmd.Parameters.AddWithValue("@isDefault", tool.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("@env", (object?)tool.EnvVariables ?? DBNull.Value);

        tool.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return tool;
    }

    public async Task UpdateToolAsync(ToolConfig tool, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE tool_configs
            SET version_name = @version, executable_path = @path, env_variables = @env
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", tool.Id);
        cmd.Parameters.AddWithValue("@version", tool.VersionName);
        cmd.Parameters.AddWithValue("@path", tool.ExecutablePath);
        cmd.Parameters.AddWithValue("@env", (object?)tool.EnvVariables ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteToolAsync(int id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tool_configs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetDefaultVersionAsync(int toolId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var transaction = conn.BeginTransaction();

        try
        {
            string? toolType = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT tool_type FROM tool_configs WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", toolId);
                toolType = (await cmd.ExecuteScalarAsync(ct))?.ToString();
            }

            if (toolType == null)
            {
                transaction.Rollback();
                return;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE tool_configs SET is_default = 0 WHERE tool_type = @type";
                cmd.Parameters.AddWithValue("@type", toolType);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE tool_configs SET is_default = 1 WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", toolId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { /* 原始异常更重要 */ }
            throw;
        }
    }

    public async Task<ToolConfig?> GetDefaultToolAsync(string toolType, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
            FROM tool_configs
            WHERE tool_type = @type AND is_default = 1
            LIMIT 1";
        cmd.Parameters.AddWithValue("@type", toolType);
        var tools = await ReadToolsAsync(cmd, ct);
        return tools.FirstOrDefault();
    }

    #region Helpers

    private static async Task<IReadOnlyList<ToolConfig>> ReadToolsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var tools = new List<ToolConfig>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tools.Add(new ToolConfig
            {
                Id = reader.GetInt32(0),
                ToolType = reader.GetString(1),
                VersionName = reader.GetString(2),
                ExecutablePath = reader.GetString(3),
                IsDefault = reader.GetInt32(4) == 1,
                EnvVariables = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6)
            });
        }
        return tools;
    }

    #endregion
}
