using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskScheduler.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private string _version = "2026.10.16.3339960";
    [ObservableProperty] private string _releaseTime = "2026-10-16 15:58:15";
    [ObservableProperty] private string _framework = ".NET + Avalonia UI";
    [ObservableProperty] private string _platforms = "Windows / Linux / macOS";
    [ObservableProperty] private string _license = "Version 2.0 License";
}
