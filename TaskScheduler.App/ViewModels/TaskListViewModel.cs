using System;
using System.IO;
using System.Linq;
using System.Text;
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

public partial class TaskListViewModel : ViewModelBase, IParameterReceiver
{
    private readonly ITaskSchedulerService _taskService;
    private readonly IExecutionLogService _logService;
    private readonly INavigationService _navigation;
    private readonly ILogger<TaskListViewModel> _logger;

    [ObservableProperty] private AvaloniaList<ScheduledTaskDetail> _tasks = new();
    [ObservableProperty] private ScheduledTaskDetail? _selectedTask;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private TriggerState? _selectedFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private ScheduledTaskDetail? _expandedTask;
    [ObservableProperty] private bool? _isAllSelected;

    public AvaloniaList<TriggerState?> FilterOptions { get; } =
    [
        null,
        TriggerState.Normal,
        TriggerState.Paused,
        TriggerState.Complete,
        TriggerState.Blocked,
        TriggerState.Error,
        TriggerState.None
    ];

    public AvaloniaList<ScheduledTaskDetail> FilteredTasks
    {
        get
        {
            var filtered = Tasks.AsEnumerable();

            // 按筛选条件
            if (SelectedFilter.HasValue)
            {
                var state = SelectedFilter.Value;
                filtered = filtered.Where(t => t.Triggers.Any(tr => tr.State == state));
            }

            // 按搜索文本
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filtered = filtered.Where(t =>
                    t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    t.Group.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (t.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            return new AvaloniaList<ScheduledTaskDetail>(filtered);
        }
    }

    public TaskListViewModel(ITaskSchedulerService taskService, IExecutionLogService logService, INavigationService navigation, ILogger<TaskListViewModel> logger)
    {
        _taskService = taskService;
        _logService = logService;
        _navigation = navigation;
        _logger = logger;
    }

    public void OnParameterReceived(object parameter)
    {
        if (parameter is string searchText)
        {
            SearchText = searchText;
        }
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredTasks));
    partial void OnSelectedFilterChanged(TriggerState? value) => OnPropertyChanged(nameof(FilteredTasks));

    [RelayCommand]
    private async Task LoadTasksAsync()
    {
        IsLoading = true;
        try
        {
            var tasks = await _taskService.GetAllTasksAsync();
            Tasks = new AvaloniaList<ScheduledTaskDetail>(tasks);
            OnPropertyChanged(nameof(FilteredTasks));
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "加载任务列表失败");
                ShowToast("加载任务列表失败", NotificationType.Error);
            }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedTask = null;
    }

    [RelayCommand]
    private void Filter(TriggerState? filter)
    {
        SelectedFilter = filter;
    }

    [RelayCommand]
    private void ViewDetail()
    {
        if (SelectedTask != null)
            _navigation.NavigateTo<TaskDetailViewModel>(SelectedTask);
    }

    [RelayCommand]
    private void EditTask()
    {
        if (SelectedTask != null)
            _navigation.NavigateTo<TaskEditorViewModel>(SelectedTask);
    }

    [RelayCommand]
    private void CreateTask()
    {
        _navigation.NavigateTo<TaskEditorViewModel>();
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (SelectedTask == null) return;
        try
        {
            await _taskService.TriggerJobAsync(SelectedTask.Name, SelectedTask.Group);
            await LoadTasksAsync();
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "立即执行任务失败: {TaskName}", SelectedTask.Name);
                ShowToast("立即执行任务失败", NotificationType.Error);
            }
            ShowToast("任务已触发执行", NotificationType.Success);
    }

    [RelayCommand]
    private async Task PauseTaskAsync()
    {
        if (SelectedTask == null) return;
        try
        {
            await _taskService.PauseTaskAsync(SelectedTask.Name, SelectedTask.Group);
            await LoadTasksAsync();
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "暂停任务失败: {TaskName}", SelectedTask.Name);
                ShowToast("暂停任务失败", NotificationType.Error);
            }
            ShowToast("任务已暂停", NotificationType.Success);
    }

    [RelayCommand]
    private async Task ResumeTaskAsync()
    {
        if (SelectedTask == null) return;
        try
        {
            await _taskService.ResumeTaskAsync(SelectedTask.Name, SelectedTask.Group);
            await LoadTasksAsync();
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "恢复任务失败: {TaskName}", SelectedTask.Name);
                ShowToast("恢复任务失败", NotificationType.Error);
            }
            ShowToast("任务已恢复", NotificationType.Success);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync()
    {
        if (SelectedTask == null) return;

        var result = await MessageBox.ShowAsync(
            $"确定要删除任务 \"{SelectedTask.Name}\" 吗？此操作不可撤销。",
            "确认删除",
            MessageBoxIcon.Warning,
            MessageBoxButton.YesNo);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _taskService.DeleteTaskAsync(SelectedTask.Name, SelectedTask.Group);
            await LoadTasksAsync();
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "删除任务失败: {TaskName}", SelectedTask.Name);
                ShowToast("删除任务失败", NotificationType.Error);
            }
            ShowToast("任务已删除", NotificationType.Success);
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        if (ExpandedTask == SelectedTask)
            ExpandedTask = null;
        else
            ExpandedTask = SelectedTask;
    }

    [RelayCommand]
    private void ToggleExpandTask(ScheduledTaskDetail? task)
    {
        if (task == null) return;
        ExpandedTask = ExpandedTask == task ? null : task;
    }

    [RelayCommand]
    private void ViewDetailTask(ScheduledTaskDetail? task)
    {
        if (task != null)
            _navigation.NavigateTo<TaskDetailViewModel>(task);
    }

    [RelayCommand]
    private void EditTaskItem(ScheduledTaskDetail? task)
    {
        if (task != null)
        {
            _logger.LogInformation("EditTaskItem: Name={Name}, Group={Group}, Triggers={Triggers}",
                task.Name, task.Group, task.Triggers?.Count);
            _navigation.NavigateTo<TaskEditorViewModel>(task);
        }
    }

    [RelayCommand]
    private async Task RunNowTaskAsync(ScheduledTaskDetail? task)
    {
        if (task == null) return;
        try
        {
            await _taskService.TriggerJobAsync(task.Name, task.Group);
            await LoadTasksAsync();
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "立即执行任务失败: {TaskName}", task.Name);
                ShowToast("立即执行任务失败", NotificationType.Error);
            }
            ShowToast("任务已触发执行", NotificationType.Success);
    }

    /// <summary>
    /// 切换任务启用/停用状态。
    /// 当前启用则暂停，当前暂停则恢复。
    /// </summary>
    [RelayCommand]
    private async Task ToggleTaskEnabledAsync(ScheduledTaskDetail? task)
    {
        if (task == null) return;
        try
        {
            if (task.IsEnabled)
            {
                await _taskService.PauseTaskAsync(task.Name, task.Group);
            }
            else
            {
                await _taskService.ResumeTaskAsync(task.Name, task.Group);
            }
            await LoadTasksAsync();
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "切换任务状态失败: {TaskName}", task.Name);
                ShowToast("切换任务状态失败", NotificationType.Error);
            }
    }

    /// <summary>
    /// 复制任务到剪贴板。
    /// </summary>
    [RelayCommand]
    private async Task CopyTaskAsync(ScheduledTaskDetail? task)
    {
        if (task == null) return;

        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard != null)
        {
            var exportModel = TaskExportModel.FromTask(task, task.CommandJson);
            var text = exportModel.ToJson();
            await topLevel.Clipboard.SetTextAsync(text);
            ShowToast("已成功复制到粘贴板", NotificationType.Success);
        }
    }

    /// <summary>
    /// 导出任务为 JSON 文件。
    /// </summary>
    [RelayCommand]
    private async Task ExportTaskAsync(ScheduledTaskDetail? task)
    {
        if (task == null) return;

        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var exportModel = TaskExportModel.FromTask(task, task.CommandJson);
        var json = exportModel.ToJson();

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出任务",
            SuggestedFileName = $"{task.Name}.json",
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } } }
        });

        if (file != null)
        {
            await File.WriteAllTextAsync(file.Path.LocalPath, json, Encoding.UTF8);
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
