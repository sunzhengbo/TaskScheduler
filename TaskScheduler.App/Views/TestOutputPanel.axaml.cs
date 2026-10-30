using Avalonia.Controls;

namespace TaskScheduler.App.Views;

/// <summary>
/// 测试输出面板的复用外壳：普通视图与最大化视图各放一份，
/// 避免在 <see cref="TaskEditorView"/> 中重复维护两套完全相同的输出区 XAML。
/// </summary>
public partial class TestOutputPanel : UserControl
{
    public TestOutputPanel()
    {
        InitializeComponent();
    }
}
