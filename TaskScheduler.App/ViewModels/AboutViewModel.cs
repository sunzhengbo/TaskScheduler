using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskScheduler.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private string _version = "0.0.1";
    [ObservableProperty] private string _releaseDate = "2025-06-04";
    [ObservableProperty] private string _framework = "Avalonia UI (.NET 10)";
    [ObservableProperty] private string _platforms = "Windows / Linux / macOS";
    [ObservableProperty] private string _license = "MIT License";

    public AboutViewModel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var ver = assembly.GetName().Version;
        if (ver != null)
            Version = $"{ver.Major}.{ver.Minor}.{ver.Build}";
    }
}