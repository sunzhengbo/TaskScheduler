using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskScheduler.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private string _version = "20260612.0947.5aa1ab1";
    [ObservableProperty] private string _releaseTime = "2026-06-12 09:47:39";
    [ObservableProperty] private string _framework = ".NET + Avalonia UI";
    [ObservableProperty] private string _platforms = "Windows / Linux / macOS";
    [ObservableProperty] private string _license = "Version 2.0 License";
}
