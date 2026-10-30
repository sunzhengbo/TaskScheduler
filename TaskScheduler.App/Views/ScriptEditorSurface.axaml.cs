using Avalonia.Controls;

using AvaloniaEdit;

namespace TaskScheduler.App.Views;

/// <summary>
/// 脚本编辑器外观（含占位提示）的复用外壳：普通视图与最大化视图各放一份，
/// 避免在 <see cref="TaskEditorView"/> 中重复维护两套完全相同的编辑器 XAML。
/// </summary>
public partial class ScriptEditorSurface : UserControl
{
    public ScriptEditorSurface()
    {
        InitializeComponent();
    }

    /// <summary>宿主视图用于订阅输入事件、读写文档的编辑器实例。</summary>
    public TextEditor Editor => InnerEditor;
}
