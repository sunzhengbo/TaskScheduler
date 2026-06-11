using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using Ursa.Controls;
using Avalonia.Controls.Notifications;

using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.ViewModels;

public partial class TaskDetailViewModel : ViewModelBase, IParameterReceiver
{
    private readonly ITaskSchedulerService _taskService;
    private readonly IExecutionLogService _logService;
    private readonly INavigationService _navigation;
    private readonly ILogger<TaskDetailViewModel> _logger;

    [ObservableProperty] private ScheduledTaskDetail? _currentTask;
    [ObservableProperty] private TaskStatistics? _statistics;
    [ObservableProperty] private AvaloniaList<ExecutionLog> _recentLogs = new();
    [ObservableProperty] private CommandModel? _command;
    [ObservableProperty] private bool _isLoading;

    public TaskDetailViewModel(ITaskSchedulerService taskService, IExecutionLogService logService, INavigationService navigation, ILogger<TaskDetailViewModel> logger)
    {
        _taskService = taskService;
        _logService = logService;
        _navigation = navigation;
        _logger = logger;
    }

    public void OnParameterReceived(object parameter)
    {
        if (parameter is ScheduledTaskDetail task)
        {
            CurrentTask = task;
            LoadDetailCommand.Execute(null);
        }
        else if (parameter is ValueTuple<string, string> nameGroup)
        {
            _ = LoadByNameCoreAsync(nameGroup.Item1, nameGroup.Item2);
        }
    }

    private async Task LoadByNameCoreAsync(string name, string group)
    {
        try
        {
            var task = await _taskService.GetTaskAsync(name, group);
            if (task != null)
            {
                CurrentTask = task;
                await LoadDetailCoreAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通过名称加载任务详情失败: {Name}.{Group}", name, group);
            ShowToast("加载任务详情失败", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task LoadDetailAsync(CancellationToken ct)
    {
        await LoadDetailCoreAsync(ct);
    }

    private async Task LoadDetailCoreAsync(CancellationToken ct = default)
    {
        if (CurrentTask == null) return;
        IsLoading = true;
        try
        {
            Statistics = await _logService.GetStatisticsAsync(CurrentTask.Name, CurrentTask.Group, ct);
            var logs = await _logService.GetLogsAsync(CurrentTask.Name, CurrentTask.Group, null, 1, 10, ct);
            RecentLogs = new AvaloniaList<ExecutionLog>(logs);

            // 反序列化命令
            try
            {
                Command = CommandModel.DeserializeFromJson(CurrentTask.CommandJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "反序列化命令失败");
                Command = null;
            }
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "加载任务详情失败: {TaskName}", CurrentTask?.Name);
                ShowToast("加载任务详情失败", NotificationType.Error);
            }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    [RelayCommand]
    private async Task PauseAsync(CancellationToken ct)
    {
        if (CurrentTask == null) return;
        await _taskService.PauseTaskAsync(CurrentTask.Name, CurrentTask.Group, ct);
        await LoadByNameCoreAsync(CurrentTask.Name, CurrentTask.Group);
        ShowToast("任务已暂停");
    }

    [RelayCommand]
    private async Task ResumeAsync(CancellationToken ct)
    {
        if (CurrentTask == null) return;
        await _taskService.ResumeTaskAsync(CurrentTask.Name, CurrentTask.Group, ct);
        await LoadByNameCoreAsync(CurrentTask.Name, CurrentTask.Group);
        ShowToast("任务已恢复");
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct)
    {
        if (CurrentTask == null) return;

        var result = await MessageBox.ShowAsync(
            $"确定要删除任务 \"{CurrentTask.Name}\" 吗？此操作不可撤销。",
            "确认删除",
            MessageBoxIcon.Warning,
            MessageBoxButton.YesNo);
        if (result != MessageBoxResult.Yes) return;

        await _taskService.DeleteTaskAsync(CurrentTask.Name, CurrentTask.Group, ct);
        ShowToast("任务已删除");
        _navigation.GoBack();
    }

    [RelayCommand]
    private void Edit()
    {
        if (CurrentTask == null) return;
        _navigation.NavigateTo<TaskEditorViewModel>(CurrentTask);
    }

    [RelayCommand]
    private void ViewAllLogs()
    {
        _navigation.NavigateTo<ExecutionLogViewModel>();
    }

    [RelayCommand]
    private async Task ExecuteNowAsync(CancellationToken ct)
    {
        if (CurrentTask == null) return;
        await _taskService.TriggerJobAsync(CurrentTask.Name, CurrentTask.Group, ct);
        await LoadByNameCoreAsync(CurrentTask.Name, CurrentTask.Group);
        ShowToast("任务已触发执行");
    }

    /// <summary>
    /// 复制整个任务（基本信息 + 触发器 + 命令）到剪贴板。
    /// </summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (CurrentTask == null) return;

        var exportModel = TaskExportModel.FromTask(CurrentTask, CurrentTask.CommandJson);
        var text = exportModel.ToJson();

        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
            ShowToast("已成功复制到粘贴板");
        }
    }

    /// <summary>
    /// 导出整个任务（基本信息 + 触发器 + 命令）为 JSON 文件。
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (CurrentTask == null) return;

        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var exportModel = TaskExportModel.FromTask(CurrentTask, CurrentTask.CommandJson);
        var json = exportModel.ToJson();

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出任务",
            SuggestedFileName = $"{CurrentTask.Name}.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } } }
        });

        if (file != null)
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, json, Encoding.UTF8);
        }
    }

    /// <summary>
    /// 复制命令内容到剪贴板。
    /// </summary>
    [RelayCommand]
    private async Task CopyCommandAsync()
    {
        if (Command == null) return;

        var text = $"# {Command.Name} ({Command.Type})\n{Command.Content}";

        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
            ShowToast("已成功复制到粘贴板");
        }
    }

    /// <summary>
    /// 导出命令为脚本文件。
    /// </summary>
    [RelayCommand]
    private async Task ExportCommandAsync()
    {
        if (Command == null || CurrentTask == null) return;

        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var ext = Command.Type switch
        {
            "PowerShell" => ".ps1",
            "Python 脚本" => ".py",
            "Shell 脚本" => ".sh",
            "Node.js 脚本" => ".js",
            _ => ".bat"
        };

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出命令",
            SuggestedFileName = $"{Command.Name}{ext}",
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = new[] { new FilePickerFileType("脚本文件") { Patterns = new[] { $"*{ext}" } } }
        });

        if (file != null)
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, Command.Content, Encoding.UTF8);
        }
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
