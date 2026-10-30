using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TaskScheduler.App.Views;

/// <summary>
/// 脚本编辑区标题行（状态文本、格式化按钮、最大化切换）的复用外壳：
/// 普通视图与最大化视图各放一份，避免 <see cref="TaskEditorView"/> 中两处重复维护。
/// </summary>
public partial class ScriptEditorToolbar : UserControl
{
    public ScriptEditorToolbar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 「格式化」被点击时转发给宿主视图：格式化需要读取编辑器当前内容并回写光标，
    /// 属于视图职责，因此这里只转发事件、不在控件内自行处理。
    /// </summary>
    public event EventHandler<RoutedEventArgs>? FormatRequested;

    private void OnFormatClick(object? sender, RoutedEventArgs e) => FormatRequested?.Invoke(this, e);
}
