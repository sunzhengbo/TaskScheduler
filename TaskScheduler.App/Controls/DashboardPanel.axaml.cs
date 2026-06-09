using Avalonia;
using Avalonia.Controls;

namespace TaskScheduler.App.Controls;

/// <summary>
/// 仪表盘面板控件，提供带标题头部和内容区域的卡片容器
/// </summary>
public partial class DashboardPanel : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<DashboardPanel, string>(nameof(Header), string.Empty);

    public static readonly StyledProperty<object?> PanelContentProperty =
        AvaloniaProperty.Register<DashboardPanel, object?>(nameof(PanelContent));

    /// <summary>
    /// 面板头部标题
    /// </summary>
    public string Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// 面板内容
    /// </summary>
    public object? PanelContent
    {
        get => GetValue(PanelContentProperty);
        set => SetValue(PanelContentProperty, value);
    }

    public DashboardPanel()
    {
        InitializeComponent();
    }
}
