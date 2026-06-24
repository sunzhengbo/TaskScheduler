using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.ObjectPool;

namespace TaskScheduler.Core.Services;

/// <summary>
/// SqliteConnection 对象池策略。
/// 创建时预打开连接，归还时关闭连接使其可复用。
/// </summary>
public class SqliteConnectionPooledObjectPolicy : IPooledObjectPolicy<SqliteConnection>
{
    private readonly string _connectionString;

    public SqliteConnectionPooledObjectPolicy(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public bool Return(SqliteConnection obj)
    {
        if (obj.State != ConnectionState.Closed)
            obj.Close();
        return true;
    }
}
