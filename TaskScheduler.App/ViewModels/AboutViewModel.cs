using CommunityToolkit.Mvvm.ComponentModel;
using TaskScheduler.Core.Models;

namespace TaskScheduler.App.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty] private string _version = AppMetadata.Version;
    [ObservableProperty] private string _releaseTime = AppMetadata.ReleaseTime;
    [ObservableProperty] private string _framework = AppMetadata.UiFramework;
    [ObservableProperty] private string _platforms = AppMetadata.Platforms;
    [ObservableProperty] private string _license = AppMetadata.License;
}
