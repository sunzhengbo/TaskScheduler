using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Messaging;
using TaskScheduler.Desktop.Models;

namespace TaskScheduler.Desktop.Services;

public class TrayIconService : IDisposable
{
    private readonly TrayIcon _trayIcon;

    public TrayIconService()
    {
        _trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "TaskScheduler",
            Menu = []
        };

        // 显示/隐藏窗口
        var showHideItem = new NativeMenuItem
        {
            Header = "显示窗口"
        };
        showHideItem.Click += (_, _) =>
            WeakReferenceMessenger.Default.Send<TokenMessages.ShowWindowMessage>();
        _trayIcon.Menu.Add(showHideItem);

        _trayIcon.Menu.Add(new NativeMenuItemSeparator());

        // 退出
        var exitItem = new NativeMenuItem
        {
            Header = "退出"
        };
        exitItem.Click += (_, _) =>
        {
            WeakReferenceMessenger.Default.Send<TokenMessages.CloseWindowMessage>();
            WeakReferenceMessenger.Default.Send<TokenMessages.ShutdownMessage>();
        };
        _trayIcon.Menu.Add(exitItem);

        // 点击托盘图标显示窗口
        _trayIcon.Clicked += (_, _) =>
            WeakReferenceMessenger.Default.Send<TokenMessages.ShowWindowMessage>();

        _trayIcon.IsVisible = true;
    }

    private WindowIcon CreateTrayIcon()
    {
        // 使用 Avalonia 的 AssetLoader 加载嵌入资源
        var assemblyName = GetType().Assembly.GetName().Name;
        var assetPath = $"avares://{assemblyName}/Assets/logo.ico";
        using var stream = AssetLoader.Open(new Uri(assetPath));

        // 使用系统默认图标或创建简单图标
        var targetBitmap = new RenderTargetBitmap(new PixelSize(16, 16));
        using (var ctx = targetBitmap.CreateDrawingContext())
        {
            ctx.DrawImage(new Bitmap(stream), new Rect(0, 0, 16, 16));
        }

        return new WindowIcon(targetBitmap);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _trayIcon.Dispose();
    }
}