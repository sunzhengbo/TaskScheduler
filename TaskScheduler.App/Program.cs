using System;
using System.Threading;
using Avalonia;

namespace TaskScheduler.App;

internal abstract class Program
{
    /// <summary>
    /// 标记是否为真实的应用程序入口启动（而非设计器预览等）。
    /// </summary>
    internal static bool IsApplicationStartup { get; private set; }

    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        IsApplicationStartup = true;
        _mutex = new Mutex(false, "TaskScheduler-App-{3F7E9C15-2A8D-4E1B-9C6F-8D3A5B2E7F1C}");
        if (!_mutex.WaitOne(0))
        {
            _mutex.Dispose();
            _mutex = null;
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _mutex.ReleaseMutex();
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
