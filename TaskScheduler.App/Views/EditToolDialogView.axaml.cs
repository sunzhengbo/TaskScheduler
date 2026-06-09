using Avalonia.Controls;

namespace TaskScheduler.App.Views;

public partial class EditToolDialogView : UserControl
{
    public EditToolDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
