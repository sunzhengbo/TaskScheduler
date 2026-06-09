using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TaskScheduler.App.Controls;

public partial class UpcomingItem : UserControl
{
    public static readonly StyledProperty<string> ItemNameProperty =
        AvaloniaProperty.Register<UpcomingItem, string>(nameof(ItemName), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<UpcomingItem, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<string> TimeTextProperty =
        AvaloniaProperty.Register<UpcomingItem, string>(nameof(TimeText), string.Empty);

    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<UpcomingItem, string>(nameof(Status), "Normal");

    public string ItemName
    {
        get => GetValue(ItemNameProperty);
        set => SetValue(ItemNameProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string TimeText
    {
        get => GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    /// <summary>Normal, Error</summary>
    public string Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public UpcomingItem()
    {
        InitializeComponent();
        UpdateDotColor();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
        {
            UpdateDotColor();
        }
    }

    private void UpdateDotColor()
    {
        var dot = this.FindControl<Border>("DotBorder");
        if (dot == null) return;

        dot.Background = Status switch
        {
            "Error" => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            _ => new SolidColorBrush(Color.FromRgb(0x17, 0xA3, 0x4A))
        };
    }
}
