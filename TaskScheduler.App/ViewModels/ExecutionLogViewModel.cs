using System;
using System.Collections.Generic;
using System.IO;
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
using Avalonia.Controls.Notifications;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.ViewModels;

public partial class ExecutionLogViewModel : ViewModelBase
{
    private readonly IExecutionLogService _logService;
    private readonly ITaskSchedulerService _taskService;
    private readonly ILogger<ExecutionLogViewModel> _logger;

    [ObservableProperty] private AvaloniaList<ExecutionLog> _logEntries = new();
    [ObservableProperty] private ExecutionLog? _selectedLog;
    [ObservableProperty] private string _selectedFilter = "全部";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _terminalTitle = "日志详情";
    [ObservableProperty] private List<string> _terminalLines = new();
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private bool _isLoading;

    public AvaloniaList<string> FilterOptions { get; } =
        ["全部", "Success", "Failed", "Timeout", "Cancelled"];

    public ExecutionLogViewModel(IExecutionLogService logService, ITaskSchedulerService taskService, ILogger<ExecutionLogViewModel> logger)
    {
        _logService = logService;
        _taskService = taskService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        IsLoading = true;
        try
        {
            var status = SelectedFilter == "全部" ? null : SelectedFilter;
            var logs = await _logService.GetLogsAsync(null, null, status, CurrentPage, 50);
            LogEntries = new AvaloniaList<ExecutionLog>(logs);

            var count = await _logService.GetLogCountAsync(null, null, status);
            TotalPages = Math.Max(1, (int)Math.Ceiling((double)count / 50));
        }
        catch (Exception ex)
            {
                _logger.LogError(ex, "加载执行日志失败");
                ShowToast("加载执行日志失败", NotificationType.Error);
            }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void SelectLog()
    {
        if (SelectedLog == null) return;
        var lines = new List<string>();
        lines.Add($"任务: {SelectedLog.JobName} ({SelectedLog.JobGroup})");
        lines.Add($"状态: {SelectedLog.Status}");
        lines.Add($"开始: {SelectedLog.StartTime:yyyy-MM-dd HH:mm:ss}");
        lines.Add($"耗时: {SelectedLog.DurationDisplay}");
        lines.Add($"退出码: {SelectedLog.ExitCode?.ToString() ?? "—"}");
        lines.Add("");
        if (!string.IsNullOrEmpty(SelectedLog.Output))
        {
            lines.Add("=== 标准输出 ===");
            lines.Add(SelectedLog.Output);
        }
        if (!string.IsNullOrEmpty(SelectedLog.Error))
        {
            lines.Add("=== 错误输出 ===");
            lines.Add(SelectedLog.Error);
        }
        TerminalLines = lines;
        TerminalTitle = $"{SelectedLog.JobName} — {SelectedLog.StartTime:yyyy-MM-dd HH:mm:ss}";
    }

    [RelayCommand]
    private async Task FilterAsync(string filter)
    {
        SelectedFilter = filter;
        CurrentPage = 1;
        await LoadLogsAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages) { CurrentPage++; await LoadLogsAsync(); }
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (CurrentPage > 1) { CurrentPage--; await LoadLogsAsync(); }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        CurrentPage = 1;
        await LoadLogsAsync();
    }

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (SelectedLog == null) return;
        var text = string.Join("\n", TerminalLines);
        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(text);
            ShowToast("已成功复制到粘贴板", NotificationType.Success);
        }
    }

    [RelayCommand]
    private async Task ExportLogAsync()
    {
        if (SelectedLog == null) return;
        var topLevel = GetTopLevel();
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出日志",
            SuggestedFileName = $"{SelectedLog.JobName}_{SelectedLog.StartTime:yyyyMMddHHmmss}.log",
            DefaultExtension = "log",
            FileTypeChoices = new[] { new FilePickerFileType("日志文件") { Patterns = new[] { "*.log", "*.txt" } } }
        });

        if (file != null)
            await File.WriteAllTextAsync(file.Path.LocalPath, string.Join("\n", TerminalLines), Encoding.UTF8);
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
