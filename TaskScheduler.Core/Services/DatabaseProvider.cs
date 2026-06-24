using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.ObjectPool;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 提供已解析的 SQLite 数据库连接字符串，并通过对象池复用连接以减少分配开销。
/// </summary>
public class DatabaseProvider
{
    private readonly ObjectPool<SqliteConnection> _pool;

    public DatabaseProvider(IConfiguration configuration)
    {
        var sqliteSetting = configuration
            .GetSection("Database:ConnectionString").Get<string>();
        ArgumentException.ThrowIfNullOrWhiteSpace(sqliteSetting);

        var index = sqliteSetting.IndexOf('=');
        if (index < 0)
        {
            throw new ArgumentException($"Invalid connection string format (missing '='): {sqliteSetting}");
        }

        var prefix = sqliteSetting[..index];
        var suffix = sqliteSetting[(index + 1)..];

        if (!Path.IsPathRooted(suffix))
        {
            var instance = configuration.GetSection("Instance").Get<string>();
            ArgumentException.ThrowIfNullOrWhiteSpace(instance);
            var appDataDir = Infra.Helpers.PathHelper.GetOsDataDir(instance);
            suffix = Path.Combine(appDataDir, suffix);
        }

        Infra.Helpers.DirectoryHelper.CreateParentDirectory(suffix);
        ConnectionString = $"{prefix}={suffix}";

        var policy = new SqliteConnectionPooledObjectPolicy(ConnectionString);
        _pool = new DefaultObjectPool<SqliteConnection>(policy);
    }

    public string ConnectionString { get; }

    /// <summary>
    /// 从对象池获取一个已打开的连接，释放时自动归还到池中。
    /// </summary>
    public async Task<PooledSqliteConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var conn = _pool.Get();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        return new PooledSqliteConnection(_pool, conn);
    }

    /// <summary>
    /// 从对象池获取一个已打开的连接（同步版本）。
    /// </summary>
    public PooledSqliteConnection GetConnection()
    {
        var conn = _pool.Get();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        return new PooledSqliteConnection(_pool, conn);
    }
}
