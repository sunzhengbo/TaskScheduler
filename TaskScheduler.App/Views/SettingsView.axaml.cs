using Avalonia.Controls;
using Avalonia.Interactivity;
using TaskScheduler.App.ViewModels;

namespace TaskScheduler.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void SettingsView_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.LoadSettingsCommand.Execute(null);
        }
        else
        {
            DataContextChanged += (_, _) =>
            {
                if (DataContext is SettingsViewModel vm2)
                {
                    vm2.LoadSettingsCommand.Execute(null);
                }
            };
        }
    }
}
