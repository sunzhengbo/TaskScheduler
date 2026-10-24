using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using TaskScheduler.App.Services;
using TaskScheduler.Core.Detectors;
using TaskScheduler.Core.Jobs;
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
    private readonly CancellationTokenSource _cts = new();

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
    [ObservableProperty] private bool _isOnStartupType;

    partial void OnIsCronTypeChanged(bool value)
    {
        if (value)
            SetTriggerTypeFlags(TriggerType.Cron);
        else if (!IsSimpleType && !IsOnStartupType)
            IsCronType = true;
    }

    partial void OnIsSimpleTypeChanged(bool value)
    {
        if (value)
            SetTriggerTypeFlags(TriggerType.Simple);
        else if (!IsCronType && !IsOnStartupType)
            IsSimpleType = true;
    }

    partial void OnIsOnStartupTypeChanged(bool value)
    {
        if (value)
            SetTriggerTypeFlags(TriggerType.OnStartup);
        else if (!IsCronType && !IsSimpleType)
            IsOnStartupType = true;
    }

    private void SetTriggerTypeFlags(TriggerType type)
    {
        IsCronType = type == TriggerType.Cron;
        IsSimpleType = type == TriggerType.Simple;
        IsOnStartupType = type == TriggerType.OnStartup;
        SelectedTriggerType = type;
    }

    partial void OnSelectedTriggerTypeChanged(TriggerType value)
    {
        SetTriggerTypeFlags(value);
    }

    public string PageTitle => IsEditMode ? "编辑任务" : "新建任务";

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(PageTitle));

    /// <summary>
    /// 重置为新建任务的空白状态。
    /// </summary>
    public void ResetToNew()
    {
        _editingTask = null;
        IsEditMode = false;
        TaskName = string.Empty;
        Group = "DEFAULT";
        Description = null;
        Priority = TaskPriority.Normal;
        UseBootTime = false;
        CronExpression = string.Empty;
        RepeatCount = -1;
        RepeatIntervalMinutes = 30;
        SetTriggerTypeFlags(TriggerType.Cron);
        TestOutput = string.Empty;
        ValidationMessage = null;
        _originalInterpreterName = null;
        InterpreterWarningMessage = null;
        HasInterpreterWarning = false;
        Command = new CommandModel { Name = "命令1", Type = CommandTypes.PowerShell };
        Command.PropertyChanged += OnCommandModelPropertyChanged;
        SelectedCommandType = CommandTypes.PowerShell;
        UpdateInterpreterVersions();
    }

    partial void OnSelectedCommandTypeChanged(string value)
    {
        Command.Type = value;
        if (string.IsNullOrWhiteSpace(Command.Content))
        {
            var normalized = CommandTypes.Normalize(value);
            if (normalized == CommandTypes.CSharp)
            {
                Command.Content = "// .NET 10 基于文件的应用 (File-based App)\n" +
                                  "// 提示：支持通过指令直接引用 NuGet 包，如：#:package Humanizer@2.14.1\n" +
                                  "using System;\n\n" +
                                  "Console.WriteLine($\"Hello from C# Task! Current time: {DateTime.Now}\");\n";
            }
        }
        UpdateInterpreterVersions();
    }

    [ObservableProperty] private AvaloniaList<string> _availableCommandTypes = [];
    [ObservableProperty] private AvaloniaList<string> _availableInterpreterVersions = [];
    [ObservableProperty] private string _selectedCommandType = CommandTypes.PowerShell;
    [ObservableProperty] private string? _effectiveInterpreterPath;
    [ObservableProperty] private string? _builtInInterpreterPath;
    [ObservableProperty] private string? _interpreterWarningMessage;
    [ObservableProperty] private bool _hasInterpreterWarning;
    private string? _originalInterpreterName;

    public AvaloniaList<string> AvailableGroups { get; } = ["DEFAULT", "SYSTEM", "USER", "DAEMON"];
    public AvaloniaList<TaskPriority> AvailablePriorities { get; } = [TaskPriority.Low, TaskPriority.Normal, TaskPriority.High];
    public AvaloniaList<TriggerType> AvailableTriggerTypes { get; } = [TriggerType.Simple, TriggerType.Cron, TriggerType.OnStartup];

    private readonly IInterpreterDetectionService _detectionService;
    private CancellationTokenSource? _detectCts;

    public TaskEditorViewModel(
        ITaskSchedulerService taskService,
        ICommandExecutor commandExecutor,
        INavigationService navigation,
        ILogger<TaskEditorViewModel> logger,
        IToolConfigService toolConfigService,
        IInterpreterDetectionService? detectionService = null)
    {
        _taskService = taskService;
        _commandExecutor = commandExecutor;
        _navigation = navigation;
        _logger = logger;
        _toolConfigService = toolConfigService;
        _detectionService = detectionService ?? InterpreterDetectionService.Default;

        // 异步加载工具配置
        _ = LoadToolsAsync();
    }

    /// <summary>
    /// 重新加载解释器工具配置列表（供跨进程工具配置变更同步调用）。
    /// </summary>
    public Task ReloadToolsAsync() => LoadToolsAsync();

    /// <summary>
    /// 从 ToolConfigService 加载工具配置，动态构建可用的命令类型和解释器版本列表。
    /// 遵循原则：仅列出在全局工具库中已配置，或本机环境变量中已存在的命令类型。
    /// </summary>
    private async Task LoadToolsAsync()
    {
        try
        {
            _allTools = await _toolConfigService.GetAllToolsAsync();

            RefreshAvailableCommandTypes();
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
    /// 根据已配置的工具库与系统环境变量，刷新可用命令类型下拉列表。
    /// </summary>
    private void RefreshAvailableCommandTypes()
    {
        var current = Command?.Type ?? SelectedCommandType;
        var commandTypes = CommandTypes.GetConfiguredOrAvailableCommandTypes(_allTools, current);
        AvailableCommandTypes = new AvaloniaList<string>(commandTypes);

        if (!AvailableCommandTypes.Contains(SelectedCommandType) && AvailableCommandTypes.Count > 0)
        {
            SelectedCommandType = AvailableCommandTypes[0];
        }
    }

    /// <summary>
    /// 当 Command 对象变更时，重新绑定属性监听并同步解释器版本列表。
    /// </summary>
    partial void OnCommandChanged(CommandModel? oldValue, CommandModel newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnCommandModelPropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnCommandModelPropertyChanged;
        }
        UpdateInterpreterVersions();
    }

    private void OnCommandModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandModel.InterpreterVersion))
        {
            UpdateEffectiveInterpreterPath();
            if (AvailableInterpreterVersions.Contains(Command.InterpreterVersion ?? string.Empty) &&
                !string.Equals(Command.InterpreterVersion, _originalInterpreterName, StringComparison.OrdinalIgnoreCase))
            {
                InterpreterWarningMessage = null;
                HasInterpreterWarning = false;
            }
        }
    }

    /// <summary>
    /// 根据当前命令的类型，从工具配置中过滤出对应的解释器版本，并更新有效解释器路径。
    /// </summary>
    private void UpdateInterpreterVersions()
    {
        var toolType = CommandTypes.MapToToolType(Command.Type);
        if (toolType == null)
        {
            // 内置类型（Cmd、VBScript）无 ToolConfig，自动检测系统解释器路径
            AvailableInterpreterVersions = [];
            RefreshFallbackInterpreterPath(Command.Type);
            Command.InterpreterVersion = null;
            InterpreterWarningMessage = null;
            HasInterpreterWarning = false;
            return;
        }

        var matchingTools = _allTools
            .Where(t => t.ToolType.Equals(toolType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingTools.Count > 0)
        {
            var versions = matchingTools
                .Select(t => t.VersionName)
                .ToList();
            AvailableInterpreterVersions = new AvaloniaList<string>(versions);

            // 获取当前任务保存的纯净版本名（兼容历史 "pwsh (C:\path)" 格式与纯名称格式）
            var currentName = Command.InterpreterName ?? Command.InterpreterVersion ?? _originalInterpreterName;
            var matched = versions.FirstOrDefault(v => v.Equals(currentName, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                Command.InterpreterVersion = matched;
                InterpreterWarningMessage = null;
                HasInterpreterWarning = false;
            }
            else
            {
                // 若当前未匹配到可用版本，优先选用该工具类型的默认版本，其次选用第一个版本
                var defaultTool = matchingTools.FirstOrDefault(t => t.IsDefault) ?? matchingTools[0];
                Command.InterpreterVersion = defaultTool.VersionName;

                var originalSpec = _originalInterpreterName ?? currentName;
                if (IsEditMode && !string.IsNullOrWhiteSpace(originalSpec))
                {
                    InterpreterWarningMessage = $"原配置的解释器版本「{originalSpec}」在全局工具库中未找到（已被重命名或删除）。当前已为您推荐全局默认工具「{defaultTool.VersionName}」，请确认并保存任务以生效。";
                    HasInterpreterWarning = true;
                }
                else
                {
                    InterpreterWarningMessage = null;
                    HasInterpreterWarning = false;
                }
            }

            UpdateEffectiveInterpreterPath();
        }
        else
        {
            // 若工具列表尚未加载完成，且当前命令已有指定的版本名称，暂不清除以防时序覆盖
            if (!_toolsLoaded.Task.IsCompleted && !string.IsNullOrWhiteSpace(Command.InterpreterVersion))
            {
                return;
            }

            // 全局未配置该工具，但本机环境变量检测到了该解释器
            AvailableInterpreterVersions = [];
            RefreshFallbackInterpreterPath(Command.Type);
            var originalSpec = _originalInterpreterName ?? Command.InterpreterName ?? Command.InterpreterVersion;
            if (IsEditMode && !string.IsNullOrWhiteSpace(originalSpec))
            {
                InterpreterWarningMessage = $"全局工具库中未配置任何「{toolType}」解释器（原指定「{originalSpec}」）。当前回退至系统环境变量路径。";
                HasInterpreterWarning = true;
            }
            else
            {
                InterpreterWarningMessage = null;
                HasInterpreterWarning = false;
            }
            Command.InterpreterVersion = null;
        }
    }

    /// <summary>
    /// 更新当前选中的解释器版本对应的真实可执行文件物理路径。
    /// </summary>
    private void UpdateEffectiveInterpreterPath()
    {
        var toolType = CommandTypes.MapToToolType(Command.Type);
        if (toolType == null)
        {
            RefreshFallbackInterpreterPath(Command.Type);
            return;
        }

        var matchingTools = _allTools
            .Where(t => t.ToolType.Equals(toolType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingTools.Count > 0)
        {
            var currentName = Command.InterpreterName ?? Command.InterpreterVersion;
            var matched = matchingTools.FirstOrDefault(t => t.VersionName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
                          ?? matchingTools.FirstOrDefault(t => t.IsDefault)
                          ?? matchingTools[0];

            EffectiveInterpreterPath = matched.ExecutablePath;
            BuiltInInterpreterPath = EffectiveInterpreterPath;
        }
        else
        {
            RefreshFallbackInterpreterPath(Command.Type);
        }
    }

    /// <summary>
    /// 异步探测回退的系统解释器物理路径，先通过系统 PATH 快速预填，再后台进行多版本扫描并回填 UI。
    /// </summary>
    private void RefreshFallbackInterpreterPath(string commandType)
    {
        // 1. 同步快速检索 PATH 环境变量，若命中立即回填，提供即时视觉反馈
        var quickPath = _detectionService.SearchInPath(commandType);
        if (!string.IsNullOrWhiteSpace(quickPath))
        {
            EffectiveInterpreterPath = quickPath;
            BuiltInInterpreterPath = quickPath;
        }
        else
        {
            EffectiveInterpreterPath = null;
            BuiltInInterpreterPath = null;
        }

        // 2. 异步深度探测（版本管理工具扫描与系统默认目录兜底），防止 UI 假死
        // 注意：不立即 Dispose 旧 CTS，避免持有旧 token 的在途后台任务在 WaitAsync 等处注册回调时抛出 ObjectDisposedException
        var oldCts = _detectCts;
        var newCts = new CancellationTokenSource();
        _detectCts = newCts;
        oldCts?.Cancel();
        var ct = newCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var detected = await _detectionService.DetectAsync(commandType, ct: ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (!ct.IsCancellationRequested && Command.Type == commandType)
                        {
                            EffectiveInterpreterPath = detected;
                            BuiltInInterpreterPath = detected;
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略被新的选择取消的探测任务
            }
            catch (ObjectDisposedException)
            {
                // 忽略令牌已被释放的情形
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台探测解释器路径异常: {CommandType}", commandType);
            }
        }, ct);
    }

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
                // 必须显式设置类型标志，因为 SelectedTriggerType 默认值可能与触发类型相同，
                // 导致 [ObservableProperty] 不触发变更
                IsCronType = trigger.Type == TriggerType.Cron;
                IsSimpleType = trigger.Type == TriggerType.Simple;
                IsOnStartupType = trigger.Type == TriggerType.OnStartup;
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
                    _originalInterpreterName = cmd.InterpreterName ?? cmd.InterpreterVersion;
                    Command = cmd;
                    SelectedCommandType = cmd.Type;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "反序列化命令失败: {CommandJson}", task.CommandJson);
            }

            // 确保工具加载完成后再刷新命令类型和解释器版本
            _toolsLoaded.Task.ContinueWith(_ =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RefreshAvailableCommandTypes();
                    UpdateInterpreterVersions();
                });
            }, _cts.Token);
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
                Command.Content, Command.Type, Command.InterpreterVersion ?? EffectiveInterpreterPath);
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
                { "RunOnStartup", (SelectedTriggerType == TriggerType.OnStartup).ToString() },
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
                    else if (SelectedTriggerType == TriggerType.OnStartup)
                    {
                        await _taskService.UpdateOnStartupTriggerAsync(trigger.Name!, trigger.Group!, ct);
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

            _cts.Cancel();
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
    private void Cancel()
    {
        _cts.Cancel();
        _navigation.GoBack();
    }

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
            _logger.LogError(ex, "从文件导入任务失败");
            ShowToast("从文件导入任务失败", NotificationType.Error);
        }
    }

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
            _logger.LogError(ex, "从剪贴板导入任务失败");
            ShowToast("从剪贴板导入任务失败", NotificationType.Error);
        }
    }

    private void ImportFromJson(string json)
    {
        var model = TaskExportModel.FromJson(json);
        if (model == null)
        {
            ShowToast("导入失败：无效的任务 JSON 格式", NotificationType.Error);
            return;
        }

        TaskName = model.Name;
        Group = model.Group;
        Description = model.Description;
        UseBootTime = model.UseBootTime;

        SetTriggerTypeFlags(model.TriggerType);
        CronExpression = model.CronExpression;
        RepeatCount = model.RepeatCount;

        if (TimeSpan.TryParse(model.RepeatInterval, out var interval))
            RepeatIntervalMinutes = interval.TotalMinutes;

        Command = new CommandModel
        {
            Name = model.Command.Name,
            Type = model.Command.Type,
            Content = model.Command.Content,
            InterpreterVersion = model.Command.InterpreterVersion,
            Description = model.Command.Description
        };
        SelectedCommandType = model.Command.Type;

        ShowToast("任务配置已从 JSON 导入");
    }

    /// <summary>
    /// 导出当前任务配置为 JSON 文件。
    /// </summary>
    [RelayCommand]
    private async Task ExportToFileAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel == null) return;

            var cleanName = string.Join("_", (TaskName ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
            var suggestedName = !string.IsNullOrWhiteSpace(cleanName) ? cleanName : "task";
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出任务",
                SuggestedFileName = $"{suggestedName}.json",
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } } }
            });

            if (file != null)
            {
                var exportModel = BuildExportModel();
                var json = exportModel.ToJson();
                await File.WriteAllTextAsync(file.Path.LocalPath, json, Encoding.UTF8);
                ShowToast("任务配置已导出");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出任务失败");
            ShowToast("导出任务失败", NotificationType.Error);
        }
    }

    /// <summary>
    /// 复制当前任务配置 JSON 到剪贴板。
    /// </summary>
    [RelayCommand]
    private async Task ExportToClipboardAsync()
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.Clipboard == null) return;

            var exportModel = BuildExportModel();
            var json = exportModel.ToJson();

            await topLevel.Clipboard.SetTextAsync(json);
            ShowToast("任务配置已复制到剪贴板");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "复制任务配置到剪贴板失败");
            ShowToast("复制任务配置到剪贴板失败", NotificationType.Error);
        }
    }

    /// <summary>
    /// 从当前编辑器的字段构建任务导出模型。
    /// </summary>
    private TaskExportModel BuildExportModel()
    {
        return new TaskExportModel
        {
            Name = TaskName,
            Group = Group,
            Description = Description,
            TriggerType = SelectedTriggerType,
            RepeatCount = RepeatCount,
            RepeatInterval = TimeSpan.FromMinutes(RepeatIntervalMinutes).ToString(),
            CronExpression = CronExpression,
            UseBootTime = UseBootTime,
            Command = new CommandExportModel
            {
                Name = Command.Name,
                Type = Command.Type,
                Content = Command.Content,
                InterpreterVersion = Command.InterpreterVersion,
                Description = Command.Description
            }
        };
    }

    private static Avalonia.Controls.TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
