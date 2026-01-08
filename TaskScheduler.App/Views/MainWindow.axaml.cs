using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using TaskScheduler.Desktop.Models;

namespace TaskScheduler.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

#if DEBUG
        this.AttachDevTools();
#endif
        WeakReferenceMessenger.Default.Register<TokenMessages.CloseWindowMessage>(this,
            (_, _) => { Close(); });
        WeakReferenceMessenger.Default.Register<TokenMessages.ShowWindowMessage>(this,
            (_, _) => { ShowWindowFromTray(); });
    }


    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void ShowWindowFromTray()
    {
        Show();
        Activate();
        WindowState = WindowState.Normal;
    }
}