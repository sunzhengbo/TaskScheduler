using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.App.ViewModels;
using TaskScheduler.App.Views;
using TaskScheduler.Core;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App;

public class App : Application
{
    internal MainWindow? MainWindowRef { get; private set; }

    /// <summary>
    /// Quartz Scheduler 就绪信号，启动完成后置为 true，失败时置为异常。
    /// 可用于需要等待 Scheduler 就绪后再执行调度操作的场景。
    /// </summary>
    internal static TaskCompletionSource<bool> SchedulerReady { get; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        this.AttachDeveloperTools();
#endif
        var services = new ServiceCollection();
        services.AddCommonServices();

        var serviceProvider = services.BuildServiceProvider();

        // 必须在解析 IScheduler 之前初始化数据库表（Quartz ADO JobStore 需要 QRTZ_* 表）
        serviceProvider.InitDb();

        // 创建托盘图标，注册时不会创建对象，只有调用时才会创建对象，所以在此调用
        serviceProvider.GetRequiredService<TrayIconService>();

        // 手动启动 Quartz Scheduler（此时 QRTZ_* 表已就绪）
        var schedulerFactory = serviceProvider.GetRequiredService<ISchedulerFactory>();
        var scheduler = Task.Run(() => schedulerFactory.GetScheduler()).GetAwaiter().GetResult();
        var schedulerLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchedulerInit");
        _ = Task.Run(async () =>
        {
            try
            {
                await scheduler.Start();
                schedulerLogger.LogInformation("Quartz Scheduler started");
                SchedulerReady.TrySetResult(true);
            }
            catch (Exception ex)
            {
                schedulerLogger.LogError(ex, "Failed to start Quartz Scheduler");
                SchedulerReady.TrySetException(ex);
            }
        });

        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>();
        MainWindowRef = mainWindow;

        var startMinimized = Environment.GetCommandLineArgs().Contains("--minimize");
        if (!startMinimized)
        {
            try
            {
                var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
                var engineJson = settingsService.GetValue("engine_settings");
                startMinimized = EngineSettings.FromJson(engineJson).StartupMinimize;
            }
            catch (Exception ex)
            {
                serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<App>()
                    .LogWarning(ex, "加载启动配置失败");
            }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (startMinimized)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            else
            {
                desktop.MainWindow = mainWindow;
            }

            WeakReferenceMessenger.Default.Register<TokenMessages.ShutdownMessage>(this,
                async void (_, _) =>
                {
                    try
                    {
                        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
                        var logger = loggerFactory?.CreateLogger<App>();

                        // 1. 标记 MainWindow 允许真正关闭
                        mainWindow.SetShuttingDown();
                        mainWindow.Close();

                        // 2. 在后台线程关闭 Quartz Scheduler（避免在 UI 线程同步阻塞导致死锁）
                        try
                        {
                            await Task.Run(async () =>
                            {
                                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                                await scheduler.Shutdown(waitForJobsToComplete: true, cts.Token);
                            });
                            logger?.LogInformation("Quartz Scheduler 已关闭");
                        }
                        catch (Exception ex)
                        {
                            logger?.LogError(ex, "关闭 Quartz Scheduler 失败，将强制退出");
                        }

                        // 3. 释放托盘图标原生资源
                        try
                        {
                            serviceProvider.GetService<TrayIconService>()?.Dispose();
                        }
                        catch
                        {
                            // 忽略
                        }

                        // 4. 释放 DI 容器（级联释放 IDisposable 服务）
                        try
                        {
                            await serviceProvider.DisposeAsync();
                        }
                        catch
                        {
                            // 忽略
                        }

                        // 5. 关闭 Serilog，刷新并释放日志文件句柄
                        try
                        {
                            await Serilog.Log.CloseAndFlushAsync();
                        }
                        catch
                        {
                            // 日志已关闭，忽略
                        }

                        // 6. 终止进程（防止 Quartz 线程池、SQLite 连接池等残留线程阻止退出）
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        // async void 回调中必须兜底 catch，否则未处理异常直接崩溃进程
                        try
                        {
                            Serilog.Log.Fatal(ex, "Shutdown handler 发生致命错误");
                        }
                        catch
                        {
                            /* 忽略 */
                        }

                        Environment.Exit(1);
                    }
                });
        }

        var factory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger<App>();
        logger.LogInformation("DotNet Environment: {Environment}",
            Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT"));
        logger.LogInformation("Application started.");

        // 从数据库加载并应用主题（SQLite 同步 API 不会阻塞 UI 线程）
        try
        {
            var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
            var appearanceJson = settingsService.GetValue("appearance_settings");
            var themeMode = AppearanceSettings.FromJson(appearanceJson).ThemeMode;
            if (!string.IsNullOrEmpty(themeMode))
            {
                var app = Current;
                if (app is not null)
                {
                    app.RequestedThemeVariant = themeMode switch
                    {
                        "Dark" => ThemeVariant.Dark,
                        "Light" => ThemeVariant.Light,
                        _ => ThemeVariant.Default
                    };
                }
            }
        }
        catch (Exception ex)
        {
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            loggerFactory?.CreateLogger<App>().LogWarning(ex, "加载主题设置失败，使用默认主题");
        }

        base.OnFrameworkInitializationCompleted();
    }
}