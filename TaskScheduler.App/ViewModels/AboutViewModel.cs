using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskScheduler.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private string _version = "2026.08.17.3d75429";
    [ObservableProperty] private string _releaseTime = "2026-08-17 13:00:00";
    [ObservableProperty] private string _framework = ".NET + Avalonia UI";
    [ObservableProperty] private string _platforms = "Windows / Linux / macOS";
    [ObservableProperty] private string _license = "Version 2.0 License";
}
