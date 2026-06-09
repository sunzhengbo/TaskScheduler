using System;
using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;

using TaskScheduler.App.Models;
using TaskScheduler.App.ViewModels;
using TaskScheduler.Core.Models;

namespace TaskScheduler.App.Dialogs;

public partial class CreateTaskDialogModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "任务名称不能为空")]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _group = "DEFAULT";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private TriggerType _triggerType;

    [ObservableProperty]
    private int _repeatCount;

    [ObservableProperty]
    private TimeSpan _repeatInterval = TimeSpan.FromMinutes(5);

    [ObservableProperty]
    private string _cronExpression = string.Empty;

    [ObservableProperty]
    private CommandModel _command = new() { Name = "命令1" };

    public Avalonia.Collections.AvaloniaList<string> AvailableGroups { get; } = new() { "DEFAULT", "SYSTEM", "USER", "DAEMON" };

    [ObservableProperty]
    private string _commandOutput = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;
}
