using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia;

using CommunityToolkit.Mvvm.ComponentModel;

using Ursa.Controls;

namespace TaskScheduler.App.ViewModels;

public abstract class ViewModelBase : ObservableValidator
{
    /// <summary>
    /// 显示 Toast 提示信息。
    /// </summary>
    /// <param name="content">提示内容。</param>
    /// <param name="type">提示类型：Success、Error、Warning、Information。</param>
    internal static void ShowToast(string content, NotificationType type = NotificationType.Success)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null
            && WindowToastManager.TryGetToastManager(desktop.MainWindow, out var manager)
            && manager != null)
        {
            manager.Show(content, type, showIcon: true);
        }
    }
}
