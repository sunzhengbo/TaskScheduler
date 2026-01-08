using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            return;
        }

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        var sqliteSetting = configuration.GetSection("Quartz:dataSource:SQLiteDS:ConnectionString").Get<string>();
        ArgumentException.ThrowIfNullOrWhiteSpace(sqliteSetting);

        var index = sqliteSetting.IndexOf('=');
        var prefix = sqliteSetting[..index];
        var suffix = sqliteSetting[(index + 1)..];

        if (!Path.IsPathRooted(suffix))
        {
            var instance = configuration.GetSection("Instance").Get<string>();
            ArgumentException.ThrowIfNullOrWhiteSpace(instance);
            var appDataDir = PathHelper.GetOsDataDir(instance);
            suffix = Path.Combine(appDataDir, suffix);
        }

        DirectoryHelper.CreateParentDirectory(suffix);

        sqliteSetting = $"{prefix}={suffix}";
        CreateQuartzDatabase(sqliteSetting, sqlText);
    }

    private static void CreateQuartzDatabase(string connectionString, string sqlText)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = new SqliteCommand(sqlText, connection);
        command.ExecuteNonQuery();
    }
}