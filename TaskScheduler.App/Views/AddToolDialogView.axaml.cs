using Avalonia.Controls;

namespace TaskScheduler.App.Views;

public partial class AddToolDialogView : UserControl
{
    public AddToolDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
