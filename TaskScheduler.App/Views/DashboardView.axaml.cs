using Avalonia.Controls;
using Avalonia.Interactivity;
using TaskScheduler.App.ViewModels;

namespace TaskScheduler.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void DashboardView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.LoadDataCommand.Execute(null);
        }
    }
}
