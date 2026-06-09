using System;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskScheduler.Core.Models;

namespace TaskScheduler.App.ViewModels;

/// <summary>
/// 工具分组，按 ToolType 聚合多个版本
/// </summary>
public partial class ToolGroupItem : ObservableObject
{
    [ObservableProperty] private string _toolType = string.Empty;
    [ObservableProperty] private string _icon = string.Empty;
    [ObservableProperty] private string _iconColor = "#3776ab";
    [ObservableProperty] private AvaloniaList<ToolConfig> _versions = new();

    public Func<ToolConfig, Task>? SetDefaultAction { get; set; }

    [RelayCommand]
    private async Task SetDefaultForGroupAsync(ToolConfig? tool)
    {
        if (tool != null && SetDefaultAction != null)
            await SetDefaultAction(tool);
    }
}
