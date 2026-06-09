using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TaskScheduler.App.Jobs;
using TaskScheduler.App.Services;
using TaskScheduler.App.ViewModels;
using TaskScheduler.App.Views;
using TaskScheduler.Core;

namespace TaskScheduler.App;

public static class ServiceCollectionExtension
{
    public static void AddCommonServices(this IServiceCollection services)
    {
        var configuration = services.AddConfiguration();

        services.AddSerilog((context, config) =>
            config.ReadFrom.Configuration(context.GetRequiredService<IConfiguration>()));

        // 添加 Microsoft.Extensions.Logging
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(); // 使用 Serilog 作为日志提供程序
        });

        services.AddSingleton<TrayIconService>();

        // 导航服务
        services.AddSingleton<INavigationService, NavigationService>();

        // Views
        services.AddSingleton<MainWindow>();

        // ViewModels
        services.AddScoped<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TaskListViewModel>();
        services.AddTransient<TaskDetailViewModel>();
        services.AddTransient<TaskEditorViewModel>();
        services.AddTransient<ExecutionLogViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();

        // App 服务
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddTransient<CommandJob>();

        // Core 层服务（Quartz + 业务服务）
        services.AddTaskScheduler(configuration);
    }

    private static IConfigurationRoot AddConfiguration(this IServiceCollection services)
    {
        var environment = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environment))
        {
#if DEBUG
                environment = "Development";
#else
                environment = "Production";
#endif
        }

        Environment.SetEnvironmentVariable("NETCORE_ENVIRONMENT", environment);

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        var configuration = builder.Build();
        services.AddSingleton<IConfiguration>(configuration);

        return configuration;
    }
}
