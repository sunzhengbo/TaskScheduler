using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using Avalonia.Controls.Notifications;

using TaskScheduler.App.Models;
using TaskScheduler.App.ViewModels;
using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.Dialogs;

public partial class EditTaskDialogViewModel : ObservableObject
{
    private readonly ScheduledTaskDetail _originalTask;
    private readonly ITaskSchedulerService _taskSchedulerService;
    private readonly ICommandExecutor _commandExecutor;
    private readonly ILogger<EditTaskDialogViewModel> _logger;

    [ObservableProperty]
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
    private CommandModel _command = new();

    [ObservableProperty]
    private string _commandOutput = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    public EditTaskDialogViewModel(ScheduledTaskDetail task, ITaskSchedulerService taskSchedulerService, ICommandExecutor commandExecutor, ILogger<EditTaskDialogViewModel> logger)
    {
        _originalTask = task;
        _taskSchedulerService = taskSchedulerService;
        _commandExecutor = commandExecutor;
        _logger = logger;

        Name = task.Name;
        Group = task.Group;
        Description = task.Description;

        LoadCommandCommand.Execute(task);
    }

    [RelayCommand]
    private async Task LoadCommandAsync(ScheduledTaskDetail task, CancellationToken ct)
    {
        try
        {
            var jobDetail = await _taskSchedulerService.GetTaskAsync(task.Name, task.Group, ct);
            if (jobDetail != null)
            {
                try
                {
                    var cmd = CommandModel.DeserializeFromJson(jobDetail.CommandJson);
                    if (cmd != null)
                    {
                        Command = cmd;
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "命令 JSON 反序列化失败: {CommandJson}", jobDetail.CommandJson);
                    ViewModelBase.ShowToast("命令 JSON 反序列化失败", NotificationType.Error);
                }
                _logger.LogInformation("已加载任务: {TaskName}", task.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载任务失败: {TaskName}", task.Name);
            ViewModelBase.ShowToast("加载任务失败", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync()
    {
        IsExecuting = true;
        CommandOutput = "正在执行...";

        try
        {
            var result = await _commandExecutor.ExecuteCommandAsync(
                Command.Content,
                Command.Type,
                Command.InterpreterPath);

            var text = $"退出码: {result.ExitCode}";
            if (!string.IsNullOrEmpty(result.Output))
                text += $"\n输出:\n{result.Output}";
            if (!string.IsNullOrEmpty(result.Error))
                text += $"\n错误:\n{result.Error}";
            CommandOutput = text;
        }
        catch (Exception ex)
        {
            CommandOutput = $"执行失败: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task SaveTaskAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow.ToString("O");
            var createdAt = _originalTask.CreatedAt?.ToUniversalTime().ToString("O")
                ?? now;
            var jobData = new Dictionary<string, object?>
            {
                { "Command", System.Text.Json.JsonSerializer.Serialize(Command) },
                { "CreatedAt", createdAt },
                { "UpdatedAt", now }
            };

            var request = new TaskUpdateRequest
            {
                Description = Description,
                JobData = jobData
            };

            await _taskSchedulerService.UpdateTaskAsync(_originalTask.Name, _originalTask.Group, request, ct);
            ViewModelBase.ShowToast("保存成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务失败: {TaskName}", _originalTask.Name);
            ViewModelBase.ShowToast("保存任务失败", NotificationType.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
    }
}
