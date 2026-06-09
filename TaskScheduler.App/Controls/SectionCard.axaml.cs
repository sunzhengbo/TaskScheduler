using Avalonia;
using Avalonia.Controls;

namespace TaskScheduler.App.Controls;

public partial class SectionCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SectionCard, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<object?> CardContentProperty =
        AvaloniaProperty.Register<SectionCard, object?>(nameof(CardContent));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? CardContent
    {
        get => GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }

    public SectionCard()
    {
        InitializeComponent();
    }
}
