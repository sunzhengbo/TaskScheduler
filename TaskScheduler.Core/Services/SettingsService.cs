namespace TaskScheduler.Core.Services;

/// <summary>
/// 应用设置服务实现（基于 SQLite）
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly DatabaseProvider _db;

    public SettingsService(DatabaseProvider db)
    {
        _db = db;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result?.ToString();
    }

    public string? GetValue(string key)
    {
        using var conn = _db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar()?.ToString();
    }

    public async Task SetValueAsync(string key, string value, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app_settings (key, value, updated_at) VALUES (@key, @value, datetime('now'))
            ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = datetime('now')";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM app_settings ORDER BY key";

        var dict = new Dictionary<string, string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            dict[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }
        return dict;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
