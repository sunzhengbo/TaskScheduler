using Avalonia.Controls;

namespace TaskScheduler.App.Views;

public partial class SettingsEnginePanelView : UserControl
{
    public SettingsEnginePanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
