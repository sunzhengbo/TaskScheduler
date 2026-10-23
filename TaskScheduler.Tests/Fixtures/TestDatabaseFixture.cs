using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Tests.Fixtures;

/// <summary>
/// 测试用 SQLite 数据库夹具，创建隔离的临时 SQLite 数据库并执行初始化建表脚本。
/// </summary>
public sealed class TestDatabaseFixture : IDisposable
{
    private readonly string _dbPath;

    public DatabaseProvider DatabaseProvider { get; }

    public TestDatabaseFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"TaskScheduler_Test_{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = $"Data Source={_dbPath}",
                ["Instance"] = "TaskSchedulerTest"
            })
            .Build();

        DatabaseProvider = new DatabaseProvider(configuration);
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var sqlText = AssemblyHelper.ReadEmbeddedResource(typeof(DatabaseProvider).Assembly, "Resources.tables_sqlite.sql");
        using var connection = new SqliteConnection(DatabaseProvider.ConnectionString);
        connection.Open();
        using var command = new SqliteCommand(sqlText, connection);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            var wal = $"{_dbPath}-wal";
            if (File.Exists(wal)) File.Delete(wal);
            var shm = $"{_dbPath}-shm";
            if (File.Exists(shm)) File.Delete(shm);
        }
        catch
        {
            // 忽略文件释放清理异常
        }
    }
}
