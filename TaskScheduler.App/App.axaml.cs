using System;
using System.Collections.Generic;
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
using Quartz.Impl.Matchers;
using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.App.ViewModels;
using TaskScheduler.App.Views;
using TaskScheduler.Core;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App;

public class App : Application
{
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
        StartSchedulerAsync(serviceProvider, schedulerFactory);

        var mainWindow = SetupMainWindow(serviceProvider);
        RegisterShutdownHandler(serviceProvider, mainWindow, schedulerFactory);
        LogStartupInfo(serviceProvider);
        ApplyTheme(serviceProvider);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 在后台启动 Quartz Scheduler，完成重调度、暂停恢复、开机任务触发等初始化流程。
    /// </summary>
    private static void StartSchedulerAsync(IServiceProvider serviceProvider, ISchedulerFactory schedulerFactory)
    {
        var schedulerLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SchedulerInit");
        _ = Task.Run(async () =>
        {
            try
            {
                var scheduler = await schedulerFactory.GetScheduler();
                var originallyPausedKeys = await RecordPausedTriggersAsync(scheduler, schedulerLogger);
                await RescheduleSimpleTriggersAsync(serviceProvider, schedulerLogger);
                await RestorePausedTriggersAsync(scheduler, originallyPausedKeys, schedulerLogger);

                // 阶段2：启动调度器（此时所有触发器已重调度完毕，不会 misfire）
                await scheduler.Start();
                schedulerLogger.LogInformation("Quartz Scheduler started");

                // 阶段3：调度器启动后，仅真实应用入口触发开机启动任务
                if (Program.IsApplicationStartup)
                    await TriggerStartupTasksAsync(serviceProvider);

                SchedulerReady.TrySetResult(true);
            }
            catch (Exception ex)
            {
                schedulerLogger.LogError(ex, "Failed to start Quartz Scheduler");
                SchedulerReady.TrySetException(ex);
            }
        });
    }

    /// <summary>
    /// 在 Scheduler 启动前记录用户原本暂停的触发器，用于重调度后恢复暂停状态。
    /// </summary>
    private static async Task<HashSet<TriggerKey>> RecordPausedTriggersAsync(
        IScheduler scheduler, ILogger logger)
    {
        var pausedKeys = new HashSet<TriggerKey>();
        try
        {
            var allJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
            foreach (var jk in allJobKeys)
            {
                var triggers = await scheduler.GetTriggersOfJob(jk);
                foreach (var t in triggers)
                {
                    try
                    {
                        var state = await scheduler.GetTriggerState(t.Key);
                        if (state == TriggerState.Paused)
                            pausedKeys.Add(t.Key);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "获取触发器 {TriggerKey} 状态失败", t.Key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取作业键列表失败");
        }

        return pausedKeys;
    }

    /// <summary>
    /// 根据设置重新计算所有 SimpleTrigger 的起始时间。
    /// </summary>
    private static async Task RescheduleSimpleTriggersAsync(
        IServiceProvider serviceProvider, ILogger logger)
    {
        try
        {
            var settingsService = serviceProvider.GetService<ISettingsService>();
            if (settingsService != null)
            {
                var engineJson = await settingsService.GetValueAsync("engine_settings");
                var engine = EngineSettings.FromJson(engineJson);
                if (engine.RescheduleOnStartup)
                {
                    var taskService = serviceProvider.GetService<ITaskSchedulerService>();
                    if (taskService != null)
                    {
                        await taskService.RescheduleAllSimpleTriggersAsync();
                    }
                    else
                    {
                        logger.LogWarning("ITaskSchedulerService 未注册，跳过 SimpleTrigger 重调度");
                    }
                }
                else
                {
                    logger.LogInformation("启动时重新调度已关闭，跳过 SimpleTrigger 重调度");
                }
            }
            else
            {
                logger.LogWarning("ISettingsService 未注册，跳过 SimpleTrigger 重调度");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "启动时重新调度 SimpleTrigger 失败");
        }
    }

    /// <summary>
    /// 重新暂停用户原本就暂停的触发器（重调度可能改变了触发器状态）。
    /// </summary>
    private static async Task RestorePausedTriggersAsync(
        IScheduler scheduler, HashSet<TriggerKey> originallyPausedKeys, ILogger logger)
    {
        foreach (var triggerKey in originallyPausedKeys)
        {
            try
            {
                await scheduler.PauseTrigger(triggerKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "恢复暂停触发器 {TriggerKey} 失败", triggerKey);
            }
        }

        if (originallyPausedKeys.Count > 0)
            logger.LogInformation("已重新暂停 {Count} 个用户暂停的触发器", originallyPausedKeys.Count);
    }

    /// <summary>
    /// 调度器启动后，手动触发所有开机启动任务。
    /// </summary>
    private static async Task TriggerStartupTasksAsync(IServiceProvider serviceProvider)
    {
        try
        {
            var taskService = serviceProvider.GetService<ITaskSchedulerService>();
            if (taskService != null)
            {
                await taskService.TriggerAllStartupTasksAsync();
            }
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("SchedulerInit");
            logger?.LogWarning(ex, "触发开机启动任务失败");
        }
    }

    /// <summary>
    /// 创建主窗口并根据配置决定是否最小化启动。
    /// </summary>
    private MainWindow SetupMainWindow(IServiceProvider serviceProvider)
    {
        var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>();

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
        }

        return mainWindow;
    }

    /// <summary>
    /// 注册应用关闭时的清理逻辑：关闭 Scheduler、释放托盘图标、释放 DI 容器、关闭日志。
    /// </summary>
    private void RegisterShutdownHandler(
        IServiceProvider serviceProvider, MainWindow mainWindow, ISchedulerFactory schedulerFactory)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            return;

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
                        var scheduler = await schedulerFactory.GetScheduler();
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
                        if (serviceProvider is IAsyncDisposable asyncDisposable)
                            await asyncDisposable.DisposeAsync();
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

    /// <summary>
    /// 记录应用启动环境信息。
    /// </summary>
    private static void LogStartupInfo(IServiceProvider serviceProvider)
    {
        var factory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger<App>();
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("DotNet Environment: {Environment}",
                Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT"));
            logger.LogInformation("Application started.");
        }
    }

    /// <summary>
    /// 从数据库加载并应用主题设置。
    /// </summary>
    private static void ApplyTheme(IServiceProvider serviceProvider)
    {
        try
        {
            var settingsService = serviceProvider.GetRequiredService<ISettingsService>();
            var appearanceJson = settingsService.GetValue("appearance_settings");
            var themeMode = AppearanceSettings.FromJson(appearanceJson).ThemeMode;
            if (!string.IsNullOrEmpty(themeMode))
            {
                Current?.RequestedThemeVariant = themeMode switch
                {
                    "Dark" => ThemeVariant.Dark,
                    "Light" => ThemeVariant.Light,
                    _ => ThemeVariant.Default
                };
            }
        }
        catch (Exception ex)
        {
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            loggerFactory?.CreateLogger<App>().LogWarning(ex, "加载主题设置失败，使用默认主题");
        }
    }
}
