using System;
using System.Threading;
using Avalonia;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App;

internal abstract class Program
{
    /// <summary>
    /// 标记是否为真实的应用程序入口启动（而非设计器预览等）。
    /// </summary>
    internal static bool IsApplicationStartup { get; private set; }

    private static Mutex? _mutex;

    [STAThread]
    public static int Main(string[] args)
    {
        IsApplicationStartup = true;
        _mutex = new Mutex(false, SchedulerBootstrapService.SchedulerMutexName);
        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        if (!acquired)
        {
            _mutex.Dispose();
            _mutex = null;
            return 0;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        finally
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
