using Avalonia.Controls;

using TaskScheduler.App.ViewModels;

namespace TaskScheduler.App.Views;

public partial class TaskEditorView : UserControl
{
    public TaskEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is TaskEditorViewModel vm)
        {
            System.Console.WriteLine(
                $"[TaskEditorView] DataContext changed: TaskName='{vm.TaskName}', Description='{vm.Description}', IsEditMode={vm.IsEditMode}");

            // 确保编译绑定能正确解析 ViewModel 属性
            // 当 View 通过 ViewLocator 创建时，DataContext 可能在控件加载后才设置
            if (!IsLoaded)
            {
                Loaded += (_, _) =>
                {
                    System.Console.WriteLine(
                        $"[TaskEditorView] Loaded, re-checking: TaskName='{vm.TaskName}', Desc='{vm.Description}'");
                    // 强制刷新绑定
                    DataContext = vm;
                };
            }
        }
    }
}
