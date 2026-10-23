using Microsoft.Data.Sqlite;
using TaskScheduler.Core.Models;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 工具配置服务实现（基于 SQLite）
/// </summary>
public class ToolConfigService(DatabaseProvider db, IIpcSyncService? ipcSyncService = null) : IToolConfigService
{
    public async Task<IReadOnlyList<ToolConfig>> GetAllToolsAsync(CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """

                                      SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
                                      FROM tool_configs
                                      ORDER BY tool_type, version_name
                          """;
        return await ReadToolsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<ToolConfig?> GetToolByIdAsync(int id, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
                          FROM tool_configs
                          WHERE id = @id
                          LIMIT 1
                          """;
        cmd.Parameters.AddWithValue("@id", id);
        var tools = await ReadToolsAsync(cmd, ct).ConfigureAwait(false);
        return tools.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ToolConfig>> GetToolsByTypeAsync(string toolType, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """

                                      SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
                                      FROM tool_configs
                                      WHERE tool_type = @type
                                      ORDER BY version_name
                          """;
        cmd.Parameters.AddWithValue("@type", toolType);
        return await ReadToolsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<ToolConfig> AddToolAsync(ToolConfig tool, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """

                                      INSERT INTO tool_configs (tool_type, version_name, executable_path, is_default, env_variables)
                                      VALUES (@type, @version, @path, @isDefault, @env);
                                      SELECT last_insert_rowid();
                          """;
        cmd.Parameters.AddWithValue("@type", tool.ToolType);
        cmd.Parameters.AddWithValue("@version", tool.VersionName);
        cmd.Parameters.AddWithValue("@path", tool.ExecutablePath);
        cmd.Parameters.AddWithValue("@isDefault", tool.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("@env", (object?)tool.EnvVariables ?? DBNull.Value);

        tool.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
        if (ipcSyncService != null)
        {
            await ipcSyncService.NotifyAsync(SyncEventType.ToolsChanged, ct: ct).ConfigureAwait(false);
        }
        return tool;
    }

    public async Task UpdateToolAsync(ToolConfig tool, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """

                                      UPDATE tool_configs
                                      SET version_name = @version, executable_path = @path, env_variables = @env
                                      WHERE id = @id
                          """;
        cmd.Parameters.AddWithValue("@id", tool.Id);
        cmd.Parameters.AddWithValue("@version", tool.VersionName);
        cmd.Parameters.AddWithValue("@path", tool.ExecutablePath);
        cmd.Parameters.AddWithValue("@env", (object?)tool.EnvVariables ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (ipcSyncService != null)
        {
            await ipcSyncService.NotifyAsync(SyncEventType.ToolsChanged, ct: ct).ConfigureAwait(false);
        }
    }

    public async Task DeleteToolAsync(int id, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tool_configs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (ipcSyncService != null)
        {
            await ipcSyncService.NotifyAsync(SyncEventType.ToolsChanged, ct: ct).ConfigureAwait(false);
        }
    }

    public async Task SetDefaultVersionAsync(int toolId, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var transaction = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            string? toolType;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = "SELECT tool_type FROM tool_configs WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", toolId);
                toolType = (await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();
            }

            if (toolType == null)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return;
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = "UPDATE tool_configs SET is_default = 0 WHERE tool_type = @type";
                cmd.Parameters.AddWithValue("@type", toolType);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = (SqliteTransaction)transaction;
                cmd.CommandText = "UPDATE tool_configs SET is_default = 1 WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", toolId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            if (ipcSyncService != null)
            {
                await ipcSyncService.NotifyAsync(SyncEventType.ToolsChanged, ct: ct).ConfigureAwait(false);
            }
        }
        catch
        {
            try { await transaction.RollbackAsync(ct).ConfigureAwait(false); } catch { /* 原始异常更重要 */ }
            throw;
        }
    }

    public async Task<ToolConfig?> GetDefaultToolAsync(string toolType, CancellationToken ct = default)
    {
        await using var pooled = await db.GetConnectionAsync(ct).ConfigureAwait(false);
        var conn = pooled.Connection;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """

                                      SELECT id, tool_type, version_name, executable_path, is_default, env_variables, created_at
                                      FROM tool_configs
                                      WHERE tool_type = @type AND is_default = 1
                                      LIMIT 1
                          """;
        cmd.Parameters.AddWithValue("@type", toolType);
        var tools = await ReadToolsAsync(cmd, ct).ConfigureAwait(false);
        return tools.FirstOrDefault();
    }

    #region Helpers

    private static async Task<IReadOnlyList<ToolConfig>> ReadToolsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var tools = new List<ToolConfig>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            tools.Add(new ToolConfig
            {
                Id = reader.GetInt32(0),
                ToolType = reader.GetString(1),
                VersionName = reader.GetString(2),
                ExecutablePath = reader.GetString(3),
                IsDefault = reader.GetInt32(4) == 1,
                EnvVariables = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc).ToLocalTime()
            });
        }
        return tools;
    }

    #endregion
}
