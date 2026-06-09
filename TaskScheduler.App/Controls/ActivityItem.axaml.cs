using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TaskScheduler.App.Controls;

public partial class ActivityItem : UserControl
{
    public static readonly StyledProperty<string> ActivityNameProperty =
        AvaloniaProperty.Register<ActivityItem, string>(nameof(ActivityName), string.Empty);

    public static readonly StyledProperty<string> DetailProperty =
        AvaloniaProperty.Register<ActivityItem, string>(nameof(Detail), string.Empty);

    public static readonly StyledProperty<string> TimeAgoProperty =
        AvaloniaProperty.Register<ActivityItem, string>(nameof(TimeAgo), string.Empty);

    public static readonly StyledProperty<string> ActivityStatusProperty =
        AvaloniaProperty.Register<ActivityItem, string>(nameof(ActivityStatus), "Success");

    public string ActivityName
    {
        get => GetValue(ActivityNameProperty);
        set => SetValue(ActivityNameProperty, value);
    }

    public string Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string TimeAgo
    {
        get => GetValue(TimeAgoProperty);
        set => SetValue(TimeAgoProperty, value);
    }

    /// <summary>Success, Failed, Timeout, Cancelled</summary>
    public string ActivityStatus
    {
        get => GetValue(ActivityStatusProperty);
        set => SetValue(ActivityStatusProperty, value);
    }

    public ActivityItem()
    {
        InitializeComponent();
        UpdateStatusBadge();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActivityStatusProperty)
        {
            UpdateStatusBadge();
        }
    }

    private void UpdateStatusBadge()
    {
        var badgeBorder = this.FindControl<Border>("BadgeBorder");
        var badgeDot = this.FindControl<Border>("BadgeDot");
        var badgeText = this.FindControl<TextBlock>("BadgeText");
        if (badgeBorder == null || badgeDot == null || badgeText == null) return;

        var (text, fgColor, bgColor) = ActivityStatus switch
        {
            "Success" => ("成功", Color.FromRgb(0x17, 0xA3, 0x4A), Color.FromArgb(0x1E, 0x17, 0xA3, 0x4A)),
            "Failed" => ("失败", Color.FromRgb(0xDC, 0x26, 0x26), Color.FromArgb(0x1E, 0xDC, 0x26, 0x26)),
            "Timeout" => ("超时", Color.FromRgb(0xA1, 0x62, 0x07), Color.FromArgb(0x1E, 0xEA, 0xB3, 0x08)),
            "Cancelled" => ("取消", Color.FromRgb(0x6B, 0x6B, 0x6B), Color.FromArgb(0x1E, 0x6B, 0x6B, 0x6B)),
            _ => ("未知", Color.FromRgb(0x6B, 0x6B, 0x6B), Color.FromArgb(0x1E, 0x6B, 0x6B, 0x6B))
        };

        badgeBorder.Background = new SolidColorBrush(bgColor);
        badgeDot.Background = new SolidColorBrush(fgColor);
        badgeText.Text = text;
        badgeText.Foreground = new SolidColorBrush(fgColor);
    }
}
