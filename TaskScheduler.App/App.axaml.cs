using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.App.ViewModels;
using TaskScheduler.App.Views;
using TaskScheduler.Core;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App;

public class App : Application
{
    /// <summary>
    /// Quartz Scheduler 就绪信号，启动完成后置为 true，失败时置为异常。
    /// 可用于需要等待 Scheduler 就绪后再执行调度操作的场景。
    /// </summary>
    internal static TaskCompletionSource<bool> SchedulerReady { get; } = new();

    /// <summary>
    /// 检测当前进程是否为 Avalonia Designer HostApp（Rider 设计器预览）。
    /// 设计器通过 dotnet.exe exec 加载 HostApp.dll 再反射加载本程序集，
    /// Environment.ProcessPath 只是 dotnet.exe，需要检查完整命令行。
    /// </summary>
    internal static bool IsDesignMode { get; } =
        Environment.CommandLine.Contains("Designer.HostApp", StringComparison.OrdinalIgnoreCase)
        || AppDomain.CurrentDomain.FriendlyName.Contains("DesignHost", StringComparison.OrdinalIgnoreCase);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        this.AttachDeveloperTools();
#endif

        // Avalonia Designer 预览模式：仅加载 XAML，不初始化 Scheduler / 数据库 / 托盘等重量级服务
        if (IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var services = new ServiceCollection();
        services.AddCommonServices();
        var serviceProvider = services.BuildServiceProvider();

        // 必须在解析 IScheduler 之前初始化数据库表（Quartz ADO JobStore 需要 QRTZ_* 表）
        serviceProvider.InitDb();

        // 创建托盘图标，注册时不会创建对象，只有调用时才会创建对象，所以在此调用
        serviceProvider.GetRequiredService<TrayIconService>();

        // 启动跨进程 IPC 同步监听服务（接收外部 CLI 命令行的变更通知并自动刷新 UI 与调度器）
        var ipcSyncService = serviceProvider.GetRequiredService<IIpcSyncService>();
        ipcSyncService.MessageReceived += msg => HandleSyncMessage(serviceProvider, msg);
        ipcSyncService.StartServer();

        // 手动启动 Quartz Scheduler（此时 QRTZ_* 表已就绪）
        var schedulerFactory = serviceProvider.GetRequiredService<ISchedulerFactory>();
        StartSchedulerAsync(serviceProvider, schedulerFactory);

        var mainWindow = SetupMainWindow(serviceProvider);
        RegisterShutdownHandler(serviceProvider, mainWindow, schedulerFactory, ipcSyncService);
        LogStartupInfo(serviceProvider);
        ApplyTheme(serviceProvider);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 处理来自外部 CLI 进程或后台作业的状态变更通知，在 UI 线程实时刷新当前视图与主题。
    /// </summary>
    private static void HandleSyncMessage(IServiceProvider serviceProvider, SyncMessage msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var navigation = serviceProvider.GetService<INavigationService>();
                var currentPage = navigation?.CurrentPage;

                if (msg.Type == SyncEventType.SettingsChanged)
                {
                    ApplyTheme(serviceProvider);
                    if (currentPage is SettingsViewModel settingsVm)
                    {
                        settingsVm.LoadSettingsCommand.Execute(null);
                    }
                    return;
                }

                if (msg.Type == SyncEventType.ToolsChanged)
                {
                    if (currentPage is SettingsViewModel settingsVm)
                    {
                        settingsVm.LoadSettingsCommand.Execute(null);
                    }
                    else if (currentPage is TaskEditorViewModel editorVm)
                    {
                        _ = editorVm.ReloadToolsAsync();
                    }
                    return;
                }

                if (msg.Type is SyncEventType.TasksChanged or SyncEventType.LogsChanged or SyncEventType.TriggerJobNow)
                {
                    switch (currentPage)
                    {
                        case DashboardViewModel dashboardVm:
                            dashboardVm.LoadDataCommand.Execute(null);
                            break;
                        case TaskListViewModel taskListVm:
                            taskListVm.LoadTasksCommand.Execute(null);
                            break;
                        case ExecutionLogViewModel logVm:
                            logVm.LoadLogsCommand.Execute(null);
                            break;
                        case TaskDetailViewModel { CurrentTask: not null } detailVm:
                            detailVm.OnParameterReceived((detailVm.CurrentTask.Name, detailVm.CurrentTask.Group));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<App>()
                    .LogWarning(ex, "处理跨进程 UI 实时刷新失败: {Type}", msg.Type);
            }
        });
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
                await SchedulerBootstrapService.StartSchedulerCoreAsync(
                    serviceProvider,
                    schedulerFactory,
                    triggerStartupTasks: Program.IsApplicationStartup);
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
        IServiceProvider serviceProvider,
        MainWindow mainWindow,
        ISchedulerFactory schedulerFactory,
        IIpcSyncService ipcSyncService)
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

                    // 0. 停止 IPC 监听服务
                    try
                    {
                        ipcSyncService.Dispose();
                    }
                    catch
                    {
                        // 忽略
                    }

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
