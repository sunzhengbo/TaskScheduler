using Avalonia;
using Avalonia.Controls;

namespace TaskScheduler.App.Controls;

public partial class StatCard : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<StatCard, string>(nameof(Label), "Label");

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<StatCard, string>(nameof(Value), "0");

    public static readonly StyledProperty<string?> SubProperty =
        AvaloniaProperty.Register<StatCard, string?>(nameof(Sub));

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

    public string? Sub
    {
        get => GetValue(SubProperty);
        set => SetValue(SubProperty, value);
    }

    public StatCard()
    {
        InitializeComponent();
    }
}
