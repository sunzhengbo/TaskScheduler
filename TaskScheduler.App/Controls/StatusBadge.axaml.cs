using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace TaskScheduler.App.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text), "Status");

    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Status), "Normal");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Success, Danger, Warn, Muted, Accent, Normal</summary>
    public string Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public StatusBadge()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateAppearance();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
        {
            UpdateAppearance();
        }
    }

    private void UpdateAppearance()
    {
        if (BadgeBorder == null || TextBlock == null) return;

        var (bg, fg) = Status switch
        {
            "Success" => (Color.FromRgb(0xE6, 0xF4, 0xEA), Color.FromRgb(0x1E, 0x8E, 0x3E)),
            "Danger" => (Color.FromRgb(0xFD, 0xE7, 0xE7), Color.FromRgb(0xD9, 0x3E, 0x3E)),
            "Warn" => (Color.FromRgb(0xFF, 0xF3, 0xCD), Color.FromRgb(0xB5, 0x8A, 0x00)),
            "Accent" => (Color.FromRgb(0xE3, 0xF2, 0xFD), Color.FromRgb(0x1A, 0x73, 0xE8)),
            "Muted" => (Color.FromRgb(0xF0, 0xF0, 0xF0), Color.FromRgb(0x80, 0x80, 0x80)),
            _ => (Color.FromRgb(0xE8, 0xEA, 0xED), Color.FromRgb(0x5F, 0x63, 0x68))
        };

        BadgeBorder.Background = new SolidColorBrush(bg);
        TextBlock.Foreground = new SolidColorBrush(fg);
    }
}
