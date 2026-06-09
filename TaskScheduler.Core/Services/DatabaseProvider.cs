using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace TaskScheduler.Core.Services;

/// <summary>
/// 提供已解析的 SQLite 数据库连接字符串
/// </summary>
public class DatabaseProvider
{
    private readonly string _connectionString;

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
        _connectionString = $"{prefix}={suffix}";
    }

    public string ConnectionString => _connectionString;

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}
