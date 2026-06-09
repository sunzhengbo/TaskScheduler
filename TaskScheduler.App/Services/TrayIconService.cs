using System;
using Avalonia.Controls;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.Messaging;
using TaskScheduler.App.Models;

namespace TaskScheduler.App.Services;

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
        var assemblyName = GetType().Assembly.GetName().Name;
        var assetPath = $"avares://{assemblyName}/Assets/logo.ico";
        using var stream = AssetLoader.Open(new Uri(assetPath));

        return new WindowIcon(stream);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _trayIcon.Dispose();
    }
}