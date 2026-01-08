using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TaskScheduler.Desktop.Services;
using TaskScheduler.Desktop.ViewModels;
using TaskScheduler.Desktop.Views;

namespace TaskScheduler.Desktop;

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

        services.AddSingleton<MainWindow>();
        services.AddScoped<MainWindowViewModel>();
    }

    private static IConfigurationRoot AddConfiguration(this IServiceCollection services)
    {
        var environment = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environment))
        {
#if DEBUG
            environment = "Development"
#else
             environment = "Production"
#endif
                ;
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