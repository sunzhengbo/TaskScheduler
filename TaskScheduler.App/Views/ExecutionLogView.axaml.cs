using Avalonia.Controls;
using Avalonia.Interactivity;
using TaskScheduler.App.ViewModels;

namespace TaskScheduler.App.Views;

public partial class ExecutionLogView : UserControl
{
    public ExecutionLogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void ExecutionLogView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExecutionLogViewModel vm)
        {
            vm.LoadLogsCommand.Execute(null);
        }
    }

    private void LogList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ExecutionLogViewModel vm)
        {
            vm.SelectLogCommand.Execute(null);
        }
    }
}
