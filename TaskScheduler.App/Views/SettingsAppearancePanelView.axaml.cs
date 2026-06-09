using Avalonia.Controls;

namespace TaskScheduler.App.Views;

public partial class SettingsAppearancePanelView : UserControl
{
    public SettingsAppearancePanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
