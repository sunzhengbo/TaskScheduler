using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.TextMate;

using TextMateSharp.Grammars;

using TaskScheduler.App.ViewModels;
using TaskScheduler.Core.Models;

using TextMateInstallation = AvaloniaEdit.TextMate.TextMate.Installation;

namespace TaskScheduler.App.Views;

public partial class TaskEditorView : UserControl
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<TaskEditorView>();

    private TaskEditorViewModel? _viewModel;
    private TextMateInstallation? _inlineInstallation;
    private TextMateInstallation? _maximizedInstallation;
    private RegistryOptions? _inlineRegistry;
    private RegistryOptions? _maximizedRegistry;
    private CompletionWindow? _inlineCompletionWindow;
    private CompletionWindow? _maximizedCompletionWindow;
    private CancellationTokenSource? _formatCancellationCts;
    private bool _isAttached;
    private bool _isSynchronizing;

    public TaskEditorView()
    {
        InitializeComponent();

        // 编辑器外壳（ScriptEditorSurface）内部持有真正的 TextEditor，事件在这里统一挂接
        InlineSurface.Editor.TextChanged += OnEditorTextChanged;
        MaximizedSurface.Editor.TextChanged += OnEditorTextChanged;
        InlineSurface.Editor.TextArea.TextEntering += OnEditorTextEntering;
        InlineSurface.Editor.TextArea.TextEntered += OnEditorTextEntered;
        MaximizedSurface.Editor.TextArea.TextEntering += OnEditorTextEntering;
        MaximizedSurface.Editor.TextArea.TextEntered += OnEditorTextEntered;

        // 工具栏外壳（ScriptEditorToolbar）只负责布局，格式化动作转发回视图处理
        InlineToolbar.FormatRequested += OnFormatButtonClick;
        MaximizedToolbar.FormatRequested += OnFormatButtonClick;

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        var viewModel = DataContext as TaskEditorViewModel;
        if (_isAttached)
        {
            SetViewModel(viewModel);
        }
        else
        {
            _viewModel = null;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        SetViewModel(DataContext as TaskEditorViewModel);
        if (_viewModel != null)
        {
            _ = _viewModel.RefreshFormatterAvailabilityAsync();
        }

        // 语法高亮依赖 TextView 与布局管线就绪，故推迟到 Loaded 优先级阶段安装；
        // 初始化阶段的异常会被全局处理器吞掉或终止进程，因此不在构造/attach 期直接装。
        Dispatcher.UIThread.Post(ConfigureTextMate, DispatcherPriority.Loaded);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _formatCancellationCts?.Cancel();
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        CloseCompletionWindow(InlineSurface.Editor.TextArea);
        CloseCompletionWindow(MaximizedSurface.Editor.TextArea);
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        DisposeTextMate(ref _inlineInstallation, ref _inlineRegistry);
        DisposeTextMate(ref _maximizedInstallation, ref _maximizedRegistry);
    }

    private void SetViewModel(TaskEditorViewModel? viewModel)
    {
        if (_viewModel == viewModel)
        {
            return;
        }

        _formatCancellationCts?.Cancel();
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SynchronizeFromViewModel();
        }

        UpdateGrammars();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizing || _viewModel == null)
        {
            return;
        }

        if (e.PropertyName == nameof(TaskEditorViewModel.InlineScriptContent))
        {
            SynchronizeFromViewModel();
        }
        else if (e.PropertyName == nameof(TaskEditorViewModel.SelectedCommandType))
        {
            UpdateGrammars();
        }
        else if (e.PropertyName == nameof(TaskEditorViewModel.IsScriptContentMaximized))
        {
            OnScriptContentMaximizedChanged();
        }
    }

    /// <summary>
    /// 最大化/还原只切换「哪一个编辑器可见」，因此只需把当前内容灌入可见编辑器，
    /// 不再维护两个编辑器之间的每键实时互抄（那会在 TextChanged 回调里替换文档实例，
    /// 破坏 TextMate 着色与光标状态，是编辑区卡死与闪退的主要来源）。
    /// </summary>
    private void OnScriptContentMaximizedChanged()
    {
        SynchronizeFromViewModel();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached)
            {
                return;
            }

            var editor = GetVisibleEditor();
            if (editor.IsEffectivelyVisible)
            {
                editor.Focus();
            }
        }, DispatcherPriority.Loaded);
    }

    private TextEditor GetVisibleEditor() =>
        _viewModel?.IsScriptContentMaximized == true ? MaximizedSurface.Editor : InlineSurface.Editor;

    private void SynchronizeFromViewModel()
    {
        if (_viewModel == null)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            SetDocumentText(GetVisibleEditor(), _viewModel.InlineScriptContent, clearUndo: true);
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isSynchronizing || _viewModel == null || sender is not TextEditor editor)
        {
            return;
        }

        // 只有当前可见的编辑器才是内容来源，隐藏编辑器（未展开的那一份）不参与同步
        if (!ReferenceEquals(editor, GetVisibleEditor()))
        {
            return;
        }

        var content = editor.Document.Text;
        if (string.Equals(_viewModel.InlineScriptContent, content, StringComparison.Ordinal))
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            _viewModel.InlineScriptContent = content;
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void OnEditorTextEntering(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || IsIdentifierCharacter(e.Text[0]))
        {
            return;
        }

        var window = GetCompletionWindow(sender);
        if (window is null)
        {
            return;
        }

        try
        {
            window.CompletionList.RequestInsertion(e);
        }
        catch (Exception ex)
        {
            // 补全插入失败不能有任何机会打断输入或冒泡成未处理异常（会直接终止进程）
            Logger.Debug(ex, "脚本编辑器自动补全插入失败，已关闭补全窗口");
            CloseCompletionWindow(sender);
        }
    }

    private void OnEditorTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text.Length != 1 || !IsIdentifierCharacter(e.Text[0]) || _viewModel == null ||
            sender is not TextArea textArea || GetCompletionWindow(sender) != null)
        {
            return;
        }

        try
        {
            ShowCompletionWindow(sender, textArea);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "脚本编辑器自动补全触发失败，已关闭补全窗口");
            CloseCompletionWindow(sender);
        }
    }

    private void ShowCompletionWindow(object? sender, TextArea textArea)
    {
        var viewModel = _viewModel;
        if (viewModel is null)
        {
            return;
        }

        var editor = ReferenceEquals(textArea, InlineSurface.Editor.TextArea) ? InlineSurface.Editor : MaximizedSurface.Editor;

        // 编辑区不可见（正处在另一种展开态或页面正在切换）时不要去弹补全窗口：
        // CompletionWindow.Show() 需要有效的 TextView 视觉行与滚动视口，否则会抛异常。
        if (!_isAttached || !editor.IsEffectivelyVisible || TopLevel.GetTopLevel(editor) == null)
        {
            return;
        }

        var keywords = GetKeywords(CommandTypes.Normalize(viewModel.SelectedCommandType));
        if (keywords.Length == 0)
        {
            return;
        }

        var document = textArea.Document;
        var caretOffset = textArea.Caret.Offset;
        if (caretOffset > document.TextLength)
        {
            return;
        }

        var startOffset = caretOffset;
        while (startOffset > 0 && IsIdentifierCharacter(document.GetCharAt(startOffset - 1)))
        {
            startOffset--;
        }

        var prefix = document.GetText(startOffset, caretOffset - startOffset);
        // 单个字符就把补全窗口弹出来会严重干扰正常输入，两个字符起才提示
        if (prefix.Length < 2)
        {
            return;
        }

        var candidates = keywords.Where(keyword => keyword.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        CloseCompletionWindow(sender);

        var window = new CompletionWindow(textArea) { StartOffset = startOffset };
        foreach (var keyword in candidates)
        {
            window.CompletionList.CompletionData.Add(new KeywordCompletionData(keyword));
        }

        SetCompletionWindow(sender, window);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(GetCompletionWindow(sender), window))
            {
                SetCompletionWindow(sender, null);
            }
        };
        window.Show();
    }

    private CompletionWindow? GetCompletionWindow(object? sender) =>
        ReferenceEquals(sender, InlineSurface.Editor.TextArea)
            ? _inlineCompletionWindow
            : ReferenceEquals(sender, MaximizedSurface.Editor.TextArea) ? _maximizedCompletionWindow : null;

    private void SetCompletionWindow(object? sender, CompletionWindow? window)
    {
        if (ReferenceEquals(sender, InlineSurface.Editor.TextArea))
        {
            _inlineCompletionWindow = window;
        }
        else if (ReferenceEquals(sender, MaximizedSurface.Editor.TextArea))
        {
            _maximizedCompletionWindow = window;
        }
    }

    private void CloseCompletionWindow(object? sender)
    {
        var window = GetCompletionWindow(sender);
        if (window is null)
        {
            return;
        }

        SetCompletionWindow(sender, null);
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "关闭脚本编辑器自动补全窗口失败");
        }
    }

    private static bool IsIdentifierCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static readonly string[] CSharpKeywords =
    [
        "async", "await", "bool", "class", "const", "else", "false", "foreach", "if", "int", "new", "null", "public",
        "return", "static", "string", "true", "using", "var", "void", "while"
    ];

    private static readonly string[] PythonKeywords =
    [
        "and", "as", "async", "await", "class", "def", "elif", "else", "False", "for", "from", "if", "import", "in",
        "is", "lambda", "None", "not", "or", "pass", "return", "True", "try", "while", "with", "yield"
    ];

    private static readonly string[] PowerShellKeywords =
    [
        "begin", "break", "catch", "class", "continue", "else", "elseif", "end", "false", "finally", "for", "foreach",
        "function", "if", "param", "process", "return", "switch", "throw", "trap", "try", "while"
    ];

    private static readonly string[] ShellKeywords =
    [
        "case", "do", "done", "elif", "else", "esac", "fi", "for", "function", "if", "in", "local", "return", "then",
        "while"
    ];

    private static readonly string[] NodeJsKeywords =
    [
        "async", "await", "break", "case", "catch", "class", "const", "continue", "else", "export", "false", "finally",
        "for", "from", "function", "if", "import", "let", "new", "null", "return", "switch", "this", "throw", "true",
        "try", "var", "while"
    ];

    private static string[] GetKeywords(string commandType) => commandType switch
    {
        CommandTypes.CSharp => CSharpKeywords,
        CommandTypes.Python => PythonKeywords,
        CommandTypes.PowerShell => PowerShellKeywords,
        CommandTypes.Shell => ShellKeywords,
        CommandTypes.NodeJs => NodeJsKeywords,
        _ => Array.Empty<string>()
    };

    private sealed class KeywordCompletionData(string keyword) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => keyword;
        public object Content => keyword;
        public object Description => keyword;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, Text);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => ConfigureTextMate();

    private void ConfigureTextMate()
    {
        if (!_isAttached)
        {
            return;
        }

        var theme = ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus;
        ConfigureEditorTextMate(InlineSurface.Editor, theme, ref _inlineInstallation, ref _inlineRegistry);
        ConfigureEditorTextMate(MaximizedSurface.Editor, theme, ref _maximizedInstallation, ref _maximizedRegistry);
    }

    private void ConfigureEditorTextMate(
        TextEditor editor,
        ThemeName theme,
        ref TextMateInstallation? installation,
        ref RegistryOptions? registry)
    {
        // 必须先解绑事件再 Dispose：语法高亮实例是异步加载主题的，
        // 已释放实例的 AppliedTheme 回调若晚一步到达，会作用在已被替换的编辑器状态上并抛异常。
        DisposeTextMate(ref installation, ref registry);

        TextMateInstallation? createdInstallation = null;
        try
        {
            var created = new RegistryOptions(theme);
            createdInstallation = editor.InstallTextMate(created);
            installation = createdInstallation;
            registry = created;
            createdInstallation.AppliedTheme += OnTextMateAppliedTheme;
            SetGrammar(created, createdInstallation);
            ApplyTextMateTheme(editor, createdInstallation);
        }
        catch (Exception ex)
        {
            // 语法高亮只是锦上添花：初始化失败就退化为纯文本编辑，绝不能让整个页面或进程挂掉
            Logger.Warning(ex, "初始化脚本编辑器语法高亮失败，已降级为纯文本编辑");

            // 安装成功但后续步骤抛异常时，已挂到编辑器上的实例必须摘掉，否则残留着色器会继续工作
            if (createdInstallation is not null)
            {
                createdInstallation.AppliedTheme -= OnTextMateAppliedTheme;
                try
                {
                    createdInstallation.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Logger.Debug(disposeEx, "释放脚本编辑器语法高亮实例失败");
                }
            }

            installation = null;
            registry = null;
        }
    }

    private void OnTextMateAppliedTheme(object? sender, TextMateInstallation installation)
    {
        // 以事件参数判定实例身份（事件契约保证参数就是被应用主题的实例，sender 无类型约束），
        // 只处理「当前仍然生效的那个实例」，忽略已废弃实例的迟到回调
        if (!_isAttached)
        {
            return;
        }

        var editor = ReferenceEquals(installation, _inlineInstallation)
            ? InlineSurface.Editor
            : ReferenceEquals(installation, _maximizedInstallation) ? MaximizedSurface.Editor : null;
        if (editor is null)
        {
            return;
        }

        try
        {
            ApplyTextMateTheme(editor, installation);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "应用脚本编辑器语法主题失败");
        }
    }

    private void UpdateGrammars()
    {
        if (_viewModel == null)
        {
            return;
        }

        if (_inlineRegistry != null && _inlineInstallation != null)
        {
            SetGrammar(_inlineRegistry, _inlineInstallation);
        }
        if (_maximizedRegistry != null && _maximizedInstallation != null)
        {
            SetGrammar(_maximizedRegistry, _maximizedInstallation);
        }
    }

    private void SetGrammar(RegistryOptions registry, TextMateInstallation installation)
    {
        try
        {
            var extension = CommandTypes.Normalize(_viewModel?.SelectedCommandType) switch
            {
                CommandTypes.CSharp => ".cs",
                CommandTypes.Python => ".py",
                CommandTypes.PowerShell => ".ps1",
                CommandTypes.Shell => ".sh",
                CommandTypes.NodeJs => ".js",
                _ => null
            };

            if (extension == null)
            {
                installation.SetGrammar(null);
                return;
            }

            var language = registry.GetLanguageByExtension(extension);
            if (language is null)
            {
                installation.SetGrammar(null);
                return;
            }

            var scope = registry.GetScopeByLanguageId(language.Id);
            installation.SetGrammar(scope);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "设置脚本编辑器语法失败，保持纯文本显示");
        }
    }

    private static void ApplyTextMateTheme(TextEditor editor, TextMateInstallation installation)
    {
        ApplyBrush(installation, "editor.background", brush => editor.Background = brush);
        ApplyBrush(installation, "editor.foreground", brush => editor.Foreground = brush);
        ApplyBrush(installation, "editorLineNumber.foreground", brush => editor.LineNumbersForeground = brush);

        // 选区色固定用与主题明暗匹配的高对比半透明色：
        // TextMate 主题里的 editor.selectionBackground 在部分内置主题中缺失或被解析成透明，
        // 直接套用会让选区完全看不见 —— 鼠标拖选、Ctrl+A 都像没反应。
        var selectionBrush = CreateFallbackSelectionBrush(editor);
        editor.TextArea.SelectionBrush = selectionBrush;
        editor.TextArea.SelectionBorder = new Pen(selectionBrush);

        if (TryGetThemeBrush(installation, "editor.lineHighlightBackground", out var currentLineBrush) && IsVisibleBrush(currentLineBrush))
        {
            editor.TextArea.TextView.CurrentLineBackground = currentLineBrush;
            editor.TextArea.TextView.CurrentLineBorder = new Pen(currentLineBrush);
        }
        else
        {
            // 当前行高亮若为透明色会连同选区一起「看不见」，这里直接清掉
            editor.TextArea.TextView.CurrentLineBackground = Brushes.Transparent;
            editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent);
        }
    }

    private static bool IsVisibleBrush(IBrush brush) => brush switch
    {
        ISolidColorBrush solid => solid.Color.A > 0x20,
        _ => true
    };

    private static IBrush CreateFallbackSelectionBrush(TextEditor editor) =>
        editor.ActualThemeVariant == ThemeVariant.Dark
            ? new SolidColorBrush(Color.FromArgb(0x66, 0x33, 0x99, 0xFF))
            : new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x6C, 0xCB));

    private static void ApplyBrush(TextMateInstallation installation, string name, Action<IBrush> apply)
    {
        if (TryGetThemeBrush(installation, name, out var brush))
        {
            apply(brush);
        }
    }

    private static bool TryGetThemeBrush(TextMateInstallation installation, string name, out IBrush brush)
    {
        if (installation.TryGetThemeColor(name, out var value) && Color.TryParse(value, out var color))
        {
            brush = new SolidColorBrush(color);
            return true;
        }

        brush = Brushes.Transparent;
        return false;
    }

    private void DisposeTextMate(ref TextMateInstallation? installation, ref RegistryOptions? registry)
    {
        if (installation is not null)
        {
            installation.AppliedTheme -= OnTextMateAppliedTheme;
            try
            {
                installation.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "释放脚本编辑器语法高亮实例失败");
            }
        }

        installation = null;
        registry = null;
    }

    /// <summary>
    /// 就地替换文本内容，绝不再整体替换 <see cref="TextDocument"/> 实例：
    /// TextMate 着色器、TextArea 光标与撤销栈都绑定在 Document 实例上，
    /// 换实例会让编辑器进入「能看见却打不进字」的状态，并在下一次输入时可能抛异常。
    /// </summary>
    private static void SetDocumentText(TextEditor editor, string text, bool clearUndo)
    {
        if (!string.Equals(editor.Document.Text, text, StringComparison.Ordinal))
        {
            editor.Document.Replace(0, editor.Document.TextLength, text);
        }

        if (clearUndo)
        {
            editor.Document.UndoStack.ClearAll();
        }
    }

    private async void OnFormatButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await FormatCurrentEditorAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "格式化脚本失败");
        }
    }

    private async void OnTaskEditorKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            // Ctrl+A 兜底：正常情况下 AvaloniaEdit 的 TextArea 会自己处理并标记 Handled，
            // 事件不会冒泡到这里；一旦编辑器内部输入处理器没接住（例如刚切换编辑器实例），
            // 这里补一次全选，避免「Ctrl+A 完全没反应」。
            if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control && TrySelectAllInVisibleEditor())
            {
                e.Handled = true;
                return;
            }

            var requiredModifiers = KeyModifiers.Control | KeyModifiers.Shift;
            if (e.Key != Key.F || (e.KeyModifiers & requiredModifiers) != requiredModifiers)
            {
                return;
            }

            e.Handled = true;
            await FormatCurrentEditorAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "处理脚本编辑器快捷键失败");
        }
    }

    private bool TrySelectAllInVisibleEditor()
    {
        if (!_isAttached)
        {
            return false;
        }

        var editor = GetVisibleEditor();
        if (!editor.IsEffectivelyVisible || !editor.TextArea.IsKeyboardFocusWithin)
        {
            return false;
        }

        try
        {
            editor.TextArea.Selection = Selection.Create(editor.TextArea, 0, editor.Document.TextLength);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "脚本编辑器全选失败");
            return false;
        }
    }

    private async Task FormatCurrentEditorAsync()
    {
        var viewModel = _viewModel;
        if (!_isAttached || viewModel is not { CanFormatScript: true })
        {
            return;
        }

        var editor = viewModel.IsScriptContentMaximized ? MaximizedSurface.Editor : InlineSurface.Editor;
        var source = editor.Document.Text;
        var commandType = CommandTypes.Normalize(viewModel.SelectedCommandType);
        var line = editor.TextArea.Caret.Line;
        var column = editor.TextArea.Caret.Column;
        // 同一时间只允许一个格式化在途：先取消上一个，避免两次结果先后写回编辑器
        var previousCts = _formatCancellationCts;
        var formatCts = new CancellationTokenSource();
        _formatCancellationCts = formatCts;
        previousCts?.Cancel();
        try
        {
            var result = await viewModel.FormatScriptAsync(source, formatCts.Token);
            if (!result.Ok || result.Output == null || !_isAttached || !ReferenceEquals(_viewModel, viewModel))
            {
                return;
            }

            if (CommandTypes.Normalize(viewModel.SelectedCommandType) != commandType ||
                !string.Equals(editor.Document.Text, source, StringComparison.Ordinal) ||
                !string.Equals(viewModel.InlineScriptContent, source, StringComparison.Ordinal))
            {
                viewModel.SetFormatterStatusMessage("格式化期间脚本已修改，已丢弃旧结果；请重新格式化。");
                return;
            }

            _isSynchronizing = true;
            try
            {
                SetDocumentText(editor, result.Output, clearUndo: false);
                var otherEditor = ReferenceEquals(editor, InlineSurface.Editor) ? MaximizedSurface.Editor : InlineSurface.Editor;
                if (otherEditor.IsEffectivelyVisible)
                {
                    SetDocumentText(otherEditor, result.Output, clearUndo: false);
                }

                viewModel.InlineScriptContent = result.Output;
            }
            finally
            {
                _isSynchronizing = false;
            }

            RestoreCaret(editor, line, column);
            editor.Focus();
        }
        finally
        {
            if (ReferenceEquals(_formatCancellationCts, formatCts))
            {
                _formatCancellationCts = null;
            }

            formatCts.Dispose();
        }
    }

    private static void RestoreCaret(TextEditor editor, int lineNumber, int columnNumber)
    {
        var line = editor.Document.GetLineByNumber(Math.Clamp(lineNumber, 1, editor.Document.LineCount));
        var offset = line.Offset + Math.Clamp(columnNumber - 1, 0, line.Length);
        editor.TextArea.ClearSelection();
        editor.TextArea.Caret.Offset = offset;
    }
}
