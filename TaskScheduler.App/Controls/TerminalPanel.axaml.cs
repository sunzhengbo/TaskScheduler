using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace TaskScheduler.App.Controls;

public partial class TerminalPanel : UserControl
{
    public static readonly StyledProperty<IEnumerable<string>> LinesProperty =
        AvaloniaProperty.Register<TerminalPanel, IEnumerable<string>>(nameof(Lines), new List<string>());

    public IEnumerable<string> Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public TerminalPanel()
    {
        InitializeComponent();
    }
}
