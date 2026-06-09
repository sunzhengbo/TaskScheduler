using Avalonia.Controls;

namespace TaskScheduler.App.Views;

public partial class SettingsToolPanelView : UserControl
{
    public SettingsToolPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
