using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Jobs;
using TaskScheduler.Core.Services;

namespace TaskScheduler.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.IgnoreDuplicates = true; // default: false
            options.Scheduling.OverWriteExistingData = true; // default: true
        });

        // 解析 SQLite 连接字符串（复用 DatabaseProvider 的逻辑）
        var dbProvider = new DatabaseProvider(configuration);
        var connectionString = dbProvider.ConnectionString;

        // 配置 Quartz 使用 SQLite ADO JobStore 实现任务持久化
        services.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.RetryInterval = TimeSpan.FromSeconds(15);
                store.UseGenericDatabase("SQLite-Microsoft", db =>
                {
                    db.ConnectionString = connectionString;
                });
                store.UseSystemTextJsonSerializer();
            });
        });

        services.AddSingleton<IScheduler>(sp =>
        {
            var factory = sp.GetRequiredService<ISchedulerFactory>();
            return Task.Run(() => factory.GetScheduler()).GetAwaiter().GetResult();
        });

        // 注册多版本管理工具探测器策略集合
        services.AddSingleton<IVersionManagerDetector, MiseDetector>();
        services.AddSingleton<IVersionManagerDetector, ScoopDetector>();
        services.AddSingleton<IVersionManagerDetector, UvDetector>();
        services.AddSingleton<IVersionManagerDetector, AsdfDetector>();
        services.AddSingleton<IVersionManagerDetector, NvmDetector>();
        services.AddSingleton<IVersionManagerDetector, FnmDetector>();
        services.AddSingleton<IVersionManagerDetector, VoltaDetector>();

        // 注册解释器环境探测聚合服务
        services.AddSingleton<IInterpreterDetectionService, InterpreterDetectionService>();

        services.AddSingleton<IIpcSyncService, IpcSyncService>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddTransient<CommandJob>();
        services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
        services.AddSingleton(dbProvider);
        services.AddSingleton<IExecutionLogService, ExecutionLogService>();
        services.AddSingleton<IToolConfigService, ToolConfigService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IScriptFormatterService, ScriptFormatterService>();

        return services;
    }
}
