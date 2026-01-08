using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskScheduler.Core;
using TaskScheduler.Desktop.Models;
using TaskScheduler.Desktop.Services;
using TaskScheduler.Desktop.ViewModels;
using TaskScheduler.Desktop.Views;

namespace TaskScheduler.Desktop;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddCommonServices();

        var serviceProvider = services.BuildServiceProvider();
        // 创建托盘图标，注册时不会创建对象，只有调用时才会创建对象，所以在此调用
        serviceProvider.GetRequiredService<TrayIconService>();
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            WeakReferenceMessenger.Default.Register<TokenMessages.ShutdownMessage>(this,
                (_, _) => { desktop.Shutdown(); });
        }

        var factory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger<App>();
        logger.LogInformation("DotNet Environment: {Environment}",
            Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT"));
        logger.LogInformation("Application started.");
        
        serviceProvider.InitDb();

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}