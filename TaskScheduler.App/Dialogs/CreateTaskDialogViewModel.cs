using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using Avalonia.Controls.Notifications;

using TaskScheduler.App.Jobs;
using TaskScheduler.App.ViewModels;
using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.Dialogs;

public partial class CreateTaskDialogViewModel(
    ITaskSchedulerService taskSchedulerService,
    ICommandExecutor commandExecutor,
    ILogger<CreateTaskDialogViewModel> logger)
    : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private string _group = "DEFAULT";

    [ObservableProperty] private string? _description;

    [ObservableProperty] private TriggerType _triggerType;

    [ObservableProperty] private int _repeatCount;

    [ObservableProperty] private TimeSpan _repeatInterval = TimeSpan.FromMinutes(5);

    [ObservableProperty] private int _repeatIntervalMinutes = 5;

    [ObservableProperty] private string _cronExpression = string.Empty;

    [ObservableProperty] private CommandModel _command = new() { Name = "命令1" };

    [ObservableProperty] private string _commandOutput = string.Empty;

    [ObservableProperty] private bool _isExecuting;

    public AvaloniaList<string> AvailableGroups { get; } = new() { "DEFAULT", "SYSTEM", "USER", "DAEMON" };

    public AvaloniaList<TriggerType> AvailableTriggerTypes { get; } =
        new() { TriggerType.Simple, TriggerType.Cron, TriggerType.OnStartup };

    public AvaloniaList<string> AvailableCommandTypes { get; } = BuildAvailableCommandTypes();

    private static AvaloniaList<string> BuildAvailableCommandTypes()
    {
        var types = new AvaloniaList<string>
        {
            CommandTypes.PowerShell, CommandTypes.Python, CommandTypes.Shell, CommandTypes.NodeJs
        };
        if (OperatingSystem.IsWindows())
        {
            types.Add(CommandTypes.Cmd);
            types.Add(CommandTypes.VBScript);
        }
        return types;
    }

    partial void OnRepeatIntervalMinutesChanged(int value)
    {
        RepeatInterval = TimeSpan.FromMinutes(value);
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync()
    {
        IsExecuting = true;
        CommandOutput = "正在执行...";

        try
        {
            var result = await commandExecutor.ExecuteCommandAsync(
                Command.Content,
                Command.Type,
                Command.InterpreterPath);

            CommandOutput = $"退出码: {result.ExitCode}\n输出:\n{result.Output}";
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
    private async Task CreateTaskAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow.ToString("O");
            var jobData = new Dictionary<string, object?>
            {
                { "Command", System.Text.Json.JsonSerializer.Serialize(Command) },
                { "CreatedAt", now },
                { "UpdatedAt", now }
            };

            var request = new TaskCreateRequest
            {
                Name = Name,
                Group = Group,
                Description = Description,
                JobType = typeof(CommandJob),
                JobData = jobData,
                TriggerType = TriggerType,
                RepeatCount = RepeatCount,
                RepeatInterval = RepeatInterval,
                CronExpression = CronExpression
            };

            await taskSchedulerService.CreateTaskAsync(request, ct);
            ViewModelBase.ShowToast("任务创建成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "创建任务失败");
            ViewModelBase.ShowToast("创建任务失败", NotificationType.Error);
        }
    }

    /// <summary>
    /// 从 JSON 字符串导入任务配置，填充表单字段。
    /// </summary>
    public void ImportFromJson(string json)
    {
        var model = TaskExportModel.FromJson(json);
        if (model == null) return;

        Name = model.Name;
        Group = model.Group;
        Description = model.Description;
        TriggerType = model.TriggerType;
        RepeatCount = model.RepeatCount;
        CronExpression = model.CronExpression;

        if (TimeSpan.TryParse(model.RepeatInterval, out var interval))
        {
            RepeatInterval = interval;
            RepeatIntervalMinutes = (int)interval.TotalMinutes;
        }

        Command = new CommandModel
        {
            Name = model.Command.Name,
            Type = model.Command.Type,
            Content = model.Command.Content,
            InterpreterVersion = model.Command.InterpreterVersion,
            Description = model.Command.Description
        };
    }

    /// <summary>
    /// 从文件导入任务配置。
    /// </summary>
    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "导入任务",
                AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } } }
            });

            if (files.Count > 0)
            {
                var json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
                ImportFromJson(json);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "从文件导入任务失败");
            ViewModelBase.ShowToast("从文件导入任务失败", NotificationType.Error);
        }
    }

    /// <summary>
    /// 从剪贴板导入任务配置。
    /// </summary>
    [RelayCommand]
    private async Task ImportFromClipboardAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.Clipboard == null) return;

            var data = await topLevel.Clipboard.TryGetTextAsync();
            if (!string.IsNullOrWhiteSpace(data))
            {
                ImportFromJson(data);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "从剪贴板导入任务失败");
            ViewModelBase.ShowToast("从剪贴板导入任务失败", NotificationType.Error);
        }
    }

    private static Avalonia.Controls.TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}