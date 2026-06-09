using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using TaskScheduler.App.ViewModels;

namespace TaskScheduler.App.Views;

public partial class TaskListView : UserControl
{
    public TaskListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void TaskListView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TaskListViewModel vm)
        {
            vm.LoadTasksCommand.Execute(null);
        }
    }

    /// <summary>
    /// 双击行 → 导航到 TaskDetail 页面
    /// </summary>
    private void TaskDataGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TaskListViewModel vm)
        {
            vm.ViewDetailCommand.Execute(null);
        }
    }
}
