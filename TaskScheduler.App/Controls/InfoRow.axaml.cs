using Avalonia;
using Avalonia.Controls;

namespace TaskScheduler.App.Controls;

public partial class InfoRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<InfoRow, string>(nameof(Label), "Label");

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<InfoRow, string>(nameof(Value), string.Empty);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public InfoRow()
    {
        InitializeComponent();
    }
}
