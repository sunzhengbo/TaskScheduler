using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.Core;

public static class ServiceProviderExtensions
{
    public static void InitDb(this IServiceProvider serviceProvider)
    {
        var assembly = typeof(ServiceProviderExtensions).Assembly;
        var sqlText = AssemblyHelper.ReadEmbeddedResource(assembly, "Resources.tables_sqlite.sql");

        if (string.IsNullOrEmpty(sqlText))
        {
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("DatabaseInit");
            logger?.LogWarning("嵌入 SQL 资源 'tables_sqlite.sql' 为空或未找到，业务表将不会被初始化");
            return;
        }

        var dbProvider = serviceProvider.GetRequiredService<DatabaseProvider>();
        CreateDatabase(dbProvider.ConnectionString, sqlText, serviceProvider);
    }

    private static void CreateDatabase(string connectionString, string sqlText, IServiceProvider serviceProvider)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            // 启用 WAL 模式以支持桌面端 App 与 CLI 并发读写访问，并迁移历史 JobType 名称
            var initAndMigrateSql = $"""
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA busy_timeout = 5000;
                {sqlText}
                UPDATE QRTZ_JOB_DETAILS
                SET JOB_CLASS_NAME = 'TaskScheduler.Core.Jobs.CommandJob, TaskScheduler.Core'
                WHERE JOB_CLASS_NAME LIKE 'TaskScheduler.App.Jobs.CommandJob%';
                """;

            using var command = new SqliteCommand(initAndMigrateSql, connection);
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("DatabaseInit");
            logger?.LogError(ex, "Failed to initialize database");
            throw;
        }
    }
}
