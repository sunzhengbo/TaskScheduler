using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.ObjectPool;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 可释放的 SqliteConnection 包装，释放时自动归还连接到对象池。
/// 使用 struct 避免堆分配。
/// </summary>
public readonly struct PooledSqliteConnection : IDisposable, IAsyncDisposable
{
    private readonly ObjectPool<SqliteConnection> _pool;

    /// <summary>获取底层的 SqliteConnection 实例。</summary>
    public SqliteConnection Connection { get; }

    internal PooledSqliteConnection(ObjectPool<SqliteConnection> pool, SqliteConnection connection)
    {
        _pool = pool;
        Connection = connection;
    }

    /// <summary>将连接归还到对象池。</summary>
    public void Dispose()
    {
        if (Connection.State != ConnectionState.Closed)
            Connection.Close();

        _pool.Return(Connection);
    }

    /// <summary>释放连接并归还到对象池。</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
