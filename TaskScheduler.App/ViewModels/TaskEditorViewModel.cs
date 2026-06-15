using System;
using System.Collections.Generic;
using System.Linq;
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
using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.ViewModels;

public partial class TaskEditorViewModel : ViewModelBase, IParameterReceiver
{
    private readonly ITaskSchedulerService _taskService;
    private readonly ICommandExecutor _commandExecutor;
    private readonly INavigationService _navigation;
    private readonly ILogger<TaskEditorViewModel> _logger;
    private readonly IToolConfigService _toolConfigService;
    private ScheduledTaskDetail? _editingTask;
    private IReadOnlyList<ToolConfig> _allTools = Array.Empty<ToolConfig>();
    private readonly TaskCompletionSource _toolsLoaded = new();

    [ObservableProperty] private string _taskName = string.Empty;
    [ObservableProperty] private string _group = "DEFAULT";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private TaskPriority _priority = TaskPriority.Normal;
    [ObservableProperty] private TriggerType _selectedTriggerType = TriggerType.Cron;
    [ObservableProperty] private int _repeatCount = -1;
    [ObservableProperty] private double _repeatIntervalMinutes = 30;
    [ObservableProperty] private string _cronExpression = string.Empty;
    [ObservableProperty] private bool _useBootTime;
    [ObservableProperty] private CommandModel _command = new() { Name = "命令1" };
    [ObservableProperty] private string _testOutput = string.Empty;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _validationMessage;

    [ObservableProperty] private bool _isCronType = true;
    [ObservableProperty] private bool _isSimpleType;

    partial void OnIsCronTypeChanged(bool value)
    {
        IsSimpleType = !value;
        SelectedTriggerType = value ? TriggerType.Cron : TriggerType.Simple;
    }

    partial void OnSelectedTriggerTypeChanged(TriggerType value)
    {
        var expected = value == TriggerType.Cron;
        if (IsCronType != expected)
            IsCronType = expected;
        if (IsSimpleType != !expected)
            IsSimpleType = !expected;
    }

    public string PageTitle => IsEditMode ? "编辑任务" : "新建任务";

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(PageTitle));

    partial void OnSelectedCommandTypeChanged(string value)
    {
        Command.Type = value;
        UpdateInterpreterVersions();
    }

    [ObservableProperty] private AvaloniaList<string> _availableCommandTypes = [];
    [ObservableProperty] private AvaloniaList<string> _availableInterpreterVersions = [];
    [ObservableProperty] private string _selectedCommandType = CommandTypes.PowerShell;

    public AvaloniaList<string> AvailableGroups { get; } = ["DEFAULT", "SYSTEM", "USER", "DAEMON"];
    public AvaloniaList<TaskPriority> AvailablePriorities { get; } = [TaskPriority.Low, TaskPriority.Normal, TaskPriority.High];
    public AvaloniaList<TriggerType> AvailableTriggerTypes { get; } = [TriggerType.Simple, TriggerType.Cron];

    public TaskEditorViewModel(
        ITaskSchedulerService taskService,
        ICommandExecutor commandExecutor,
        INavigationService navigation,
        ILogger<TaskEditorViewModel> logger,
        IToolConfigService toolConfigService)
    {
        _taskService = taskService;
        _commandExecutor = commandExecutor;
        _navigation = navigation;
        _logger = logger;
        _toolConfigService = toolConfigService;

        // 异步加载工具配置
        _ = LoadToolsAsync();
    }

    /// <summary>
    /// 从 ToolConfigService 加载工具配置，动态构建可用的命令类型和解释器版本列表。
    /// </summary>
    private async Task LoadToolsAsync()
    {
        try
        {
            _allTools = await _toolConfigService.GetAllToolsAsync();

            // 命令类型与设置面板的工具类型保持一致
            AvailableCommandTypes = new AvaloniaList<string>(
                ["Python", "PowerShell", "Node.js", "Bash"]);

            UpdateInterpreterVersions();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载工具配置失败");
        }
        finally
        {
            _toolsLoaded.TrySetResult();
        }
    }

    /// <summary>
    /// 当 Command 对象变更时，同步解释器版本列表。
    /// </summary>
    partial void OnCommandChanged(CommandModel value)
    {
        UpdateInterpreterVersions();
    }

    /// <summary>
    /// 根据当前命令的类型，从工具配置中过滤出对应的解释器版本。
    /// </summary>
    private void UpdateInterpreterVersions()
    {
        var toolType = MapCommandTypeToToolType(Command.Type);
        if (toolType == null)
        {
            // Cmd 等内置类型无需选择解释器版本
            AvailableInterpreterVersions = [];
            return;
        }

        var versions = _allTools
            .Where(t => t.ToolType == toolType)
            .Select(t => $"{t.VersionName} ({t.ExecutablePath})")
            .ToList();
        AvailableInterpreterVersions = new AvaloniaList<string>(versions);

        // 当前解释器版本不在新列表中时，重置为第一个可用版本
        if (versions.Count == 0 || Command.InterpreterVersion == null || !versions.Contains(Command.InterpreterVersion))
            Command.InterpreterVersion = versions.Count > 0 ? versions[0] : null;
    }

    private static string? MapCommandTypeToToolType(string commandType) => commandType switch
    {
        CommandTypes.Python or "Python 脚本" => "Python",
        CommandTypes.PowerShell or "PowerShell" => "PowerShell",
        CommandTypes.NodeJs or "Node.js 脚本" => "Node.js",
        CommandTypes.Shell or "Shell 脚本" or "Shell" => "Bash",
        _ => null // Cmd 等内置类型无对应 ToolConfig
    };

    public void OnParameterReceived(object parameter)
    {
        if (parameter is ScheduledTaskDetail task)
        {
            _logger.LogInformation("OnParameterReceived: Name={Name}, Group={Group}, Desc={Desc}, Priority={Priority}, UseBoot={UseBoot}, Triggers={Triggers}",
                task.Name, task.Group, task.Description, task.Priority, task.UseBootTime, task.Triggers?.Count);

            Console.WriteLine($"[TaskEditorVM] OnParameterReceived: Name='{task.Name}', Desc='{task.Description}'");

            _editingTask = task;
            IsEditMode = true;
            TaskName = task.Name;
            Group = task.Group;
            Description = task.Description;
            Priority = task.Priority;
            UseBootTime = task.UseBootTime;

            // 从触发器加载
            if (task.Triggers is { Count: > 0 })
            {
                var trigger = task.Triggers[0];
                // 必须显式设置 IsCronType，因为 SelectedTriggerType 默认值可能与触发类型相同，
                // 导致 [ObservableProperty] 不触发 OnSelectedTriggerTypeChanged
                IsCronType = trigger.Type == TriggerType.Cron;
                SelectedTriggerType = trigger.Type;
                CronExpression = trigger.Type == TriggerType.Cron ? trigger.TriggerDisplay : string.Empty;
                RepeatCount = trigger.RepeatCount;
                if (trigger.RepeatInterval.HasValue)
                    RepeatIntervalMinutes = trigger.RepeatInterval.Value.TotalMinutes;
            }

            // 从 CommandJson 加载命令
            try
            {
                var cmd = CommandModel.DeserializeFromJson(task.CommandJson);
                if (cmd != null)
                {
                    Command = cmd;
                    SelectedCommandType = cmd.Type;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "反序列化命令失败: {CommandJson}", task.CommandJson);
            }

            // 确保工具加载完成后再刷新解释器版本
            _toolsLoaded.Task.ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdateInterpreterVersions();
                });
            });
        }
    }

    [RelayCommand]
    private async Task TestRunAsync()
    {
        IsTesting = true;
        TestOutput = "正在执行...";
        try
        {
            var result = await _commandExecutor.ExecuteCommandAsync(
                Command.Content, Command.Type, Command.InterpreterPath);
            var text = $"退出码: {result.ExitCode}";
            if (!string.IsNullOrEmpty(result.Output))
                text += $"\n输出:\n{result.Output}";
            if (!string.IsNullOrEmpty(result.Error))
                text += $"\n错误:\n{result.Error}";
            TestOutput = text;
        }
        catch (Exception ex)
        {
            TestOutput = $"执行失败: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        // 验证必填字段
        ValidationMessage = null;
        if (string.IsNullOrWhiteSpace(TaskName))
        {
            ValidationMessage = "请输入任务名称";
            return;
        }

        if (IsCronType && string.IsNullOrWhiteSpace(CronExpression))
        {
            ValidationMessage = "请输入 Cron 表达式";
            return;
        }

        if (IsSimpleType && RepeatIntervalMinutes < 1)
        {
            ValidationMessage = "间隔时间不能小于 1 分钟";
            return;
        }

        if (string.IsNullOrWhiteSpace(Command.Content))
        {
            ValidationMessage = "请输入命令内容";
            return;
        }

        IsSaving = true;
        try
        {
            var commandJson = System.Text.Json.JsonSerializer.Serialize(Command);
            var now = DateTime.UtcNow.ToString("O");
            var createdAt = (IsEditMode && _editingTask?.CreatedAt != null)
                ? _editingTask.CreatedAt.Value.ToUniversalTime().ToString("O")
                : now;
            var jobData = new Dictionary<string, object?>
            {
                { "Command", commandJson },
                { "Priority", Priority.ToString() },
                { "UseBootTime", UseBootTime.ToString() },
                { "CreatedAt", createdAt },
                { "UpdatedAt", now }
            };

            if (IsEditMode && _editingTask != null)
            {
                var request = new TaskUpdateRequest
                {
                    Description = Description,
                    JobData = jobData
                };
                await _taskService.UpdateTaskAsync(_editingTask.Name, _editingTask.Group, request, ct);

                // 更新触发器配置
                if (_editingTask.Triggers.Count > 0)
                {
                    var trigger = _editingTask.Triggers[0];
                    if (SelectedTriggerType == TriggerType.Cron)
                    {
                        if (!string.IsNullOrWhiteSpace(CronExpression))
                            await _taskService.UpdateCronTriggerAsync(trigger.Name!, trigger.Group!, CronExpression, ct);
                    }
                    else
                    {
                        await _taskService.UpdateSimpleTriggerAsync(
                            trigger.Name!, trigger.Group!,
                            RepeatCount, TimeSpan.FromMinutes(RepeatIntervalMinutes), ct, UseBootTime);
                    }
                }
            }
            else
            {
                var request = new TaskCreateRequest
                {
                    Name = TaskName,
                    Group = Group,
                    Description = Description,
                    JobType = typeof(CommandJob),
                    JobData = jobData,
                    TriggerType = SelectedTriggerType,
                    RepeatCount = RepeatCount,
                    RepeatInterval = TimeSpan.FromMinutes(RepeatIntervalMinutes),
                    CronExpression = CronExpression,
                    UseBootTime = UseBootTime
                };
                await _taskService.CreateTaskAsync(request, ct);
            }

            ShowToast("保存成功");
            _navigation.GoBack();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务失败");
            ShowToast($"保存失败: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task CopyInterpreterPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(path);
            }
        }
    }

    [RelayCommand]
    private void Cancel() => _navigation.GoBack();
}
