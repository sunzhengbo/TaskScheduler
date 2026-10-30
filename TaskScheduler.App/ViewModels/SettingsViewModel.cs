using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;
using TaskScheduler.Infra.Helpers;

namespace TaskScheduler.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    // 模型默认值的单一事实来源：字段初始化与 ResetDefaults 均从此处取值，
    // 模型默认值变更后界面初始态与「恢复默认」自动跟随。
    private static readonly EngineSettings EngineDefaults = new();
    private static readonly AppearanceSettings AppearanceDefaults = new();

    private readonly IToolConfigService _toolService;
    private readonly ISettingsService _settingsService;
    private readonly IScriptFormatterService _formatterService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private AvaloniaList<ToolConfig> _tools = new();
    [ObservableProperty] private AvaloniaList<ToolGroupItem> _toolGroups = new();
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private bool _isSaving;

    public string SaveButtonText => IsSaving ? "保存中..." : "保存设置";

    // 引擎设置
    [ObservableProperty] private bool _startupEnabled = EngineDefaults.StartupEnabled;
    [ObservableProperty] private bool _startupMinimize = EngineDefaults.StartupMinimize;
    [ObservableProperty] private bool _rescheduleOnStartup = EngineDefaults.RescheduleOnStartup;

    partial void OnStartupEnabledChanged(bool value)
    {
        if (!value)
            StartupMinimize = false;
    }
    [ObservableProperty] private string _logRetentionDays = EngineDefaults.LogRetentionDays.ToString(CultureInfo.InvariantCulture);
    [ObservableProperty] private string _maxThreads = EngineDefaults.MaxThreads.ToString(CultureInfo.InvariantCulture);
    [ObservableProperty] private string _databaseType = EngineDefaults.DatabaseType;

    // 外观设置
    [ObservableProperty] private string _themeMode = AppearanceDefaults.ThemeMode;
    [ObservableProperty] private bool _compactMode = AppearanceDefaults.CompactMode;

    // Formatter configuration
    [ObservableProperty]
    private string _formatterTimeoutSeconds =
        ScriptFormatterDefaults.DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    [ObservableProperty] private string _blackFormatterPath = string.Empty;
    [ObservableProperty] private string _psscriptAnalyzerModulePath = string.Empty;
    [ObservableProperty] private string _shfmtFormatterPath = string.Empty;
    [ObservableProperty] private string _prettierPackagePath = string.Empty;
    [ObservableProperty] private string _formatterStatusMessage = string.Empty;
    [ObservableProperty] private bool _isCheckingFormatters;

    // 添加工具对话框
    [ObservableProperty] private bool _isAddToolDialogOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolCommand))]
    [NotifyPropertyChangedFor(nameof(IsToolNameDuplicate))]
    [NotifyPropertyChangedFor(nameof(ToolNameErrorText))]
    [NotifyPropertyChangedFor(nameof(HasToolNameError))]
    private string _newToolType = "Python";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolCommand))]
    [NotifyPropertyChangedFor(nameof(IsToolNameDuplicate))]
    [NotifyPropertyChangedFor(nameof(ToolNameErrorText))]
    [NotifyPropertyChangedFor(nameof(HasToolNameError))]
    private string _newToolVersion = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToolCommand))]
    private string _newToolPath = string.Empty;

    [ObservableProperty] private string _newToolEnvVariables = string.Empty;

    public bool IsToolNameDuplicate => !string.IsNullOrEmpty(ToolNameErrorText);

    public string? ToolNameErrorText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewToolVersion)) return null;
            var trimmed = NewToolVersion.Trim();

            var sameType = Tools.FirstOrDefault(t =>
                t.ToolType.Equals(NewToolType, StringComparison.OrdinalIgnoreCase) &&
                t.VersionName?.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase) == true);
            if (sameType != null)
                return $"【{NewToolType}】下已存在同名版本 '{trimmed}'";

            var anyType = Tools.FirstOrDefault(t =>
                t.VersionName?.Trim().Equals(trimmed, StringComparison.OrdinalIgnoreCase) == true);
            if (anyType != null)
                return $"已存在同名工具版本 '{trimmed}'（所属类型: {anyType.ToolType}）";

            return null;
        }
    }

    public bool HasToolNameError => !string.IsNullOrEmpty(ToolNameErrorText);

    public bool CanAddTool =>
        !string.IsNullOrWhiteSpace(NewToolVersion) &&
        !string.IsNullOrWhiteSpace(NewToolPath) &&
        !IsToolNameDuplicate;

    // 编辑工具对话框
    [ObservableProperty] private bool _isEditToolDialogOpen;
    [ObservableProperty] private string _editToolType = string.Empty;
    [ObservableProperty] private string _editToolIcon = string.Empty;
    [ObservableProperty] private string _editToolIconColor = "#3776ab";
    [ObservableProperty] private AvaloniaList<ToolConfig> _editingVersions = new();

    public AvaloniaList<string> ToolTypes { get; } = ["Python", "PowerShell", "Node.js", "Bash", "DotNet"];
    public AvaloniaList<string> DatabaseTypes { get; } = ["SQLite"];
    public AvaloniaList<string> ThemeModes { get; } = ["System", "Light", "Dark"];

    /// <summary>Prettier 路径输入框提示，版本要求取自 Core 常量，避免界面与实现各写一份。</summary>
    public string PrettierRequirementHint =>
        $"包目录或 bin/prettier.cjs；要求版本 ≥ {ScriptFormatterDefaults.MinimumPrettierVersion}";

    /// <summary>
    /// 超时输入框的占位提示。默认值同样取自 <see cref="ScriptFormatterDefaults"/>，
    /// 否则 XAML 里会形成与常量脱节的第二份副本。
    /// 保持实例属性：XAML 用 <c>{Binding}</c> 取值，改成 <see langword="static"/> 会让绑定静默失效
    /// （分析器会提示 CA1822，此处按上述理由不采纳）。
    /// </summary>
    public string FormatterTimeoutHint =>
        ScriptFormatterDefaults.DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture);

    public SettingsViewModel(
        IToolConfigService toolService,
        ISettingsService settingsService,
        IScriptFormatterService formatterService,
        ILogger<SettingsViewModel> logger)
    {
        _toolService = toolService;
        _settingsService = settingsService;
        _formatterService = formatterService;
        _logger = logger;
        _logger.LogDebug("SettingsViewModel 已创建");
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        IsLoading = true;
        try
        {
            var tools = await _toolService.GetAllToolsAsync();
            Tools = new AvaloniaList<ToolConfig>(tools);
            ToolGroups = BuildToolGroups(tools);

            // 迁移旧数据：读取旧 key，转换为新格式后删除
            await MigrateOldSettingsAsync();

            var engineJson = await _settingsService.GetValueAsync("engine_settings");
            if (!string.IsNullOrEmpty(engineJson))
            {
                try
                {
                    var engine = EngineSettings.FromJson(engineJson);
                    LogRetentionDays = engine.LogRetentionDays.ToString(CultureInfo.InvariantCulture);
                    MaxThreads = engine.MaxThreads.ToString(CultureInfo.InvariantCulture);
                    DatabaseType = engine.DatabaseType;
                    StartupEnabled = engine.StartupEnabled;
                    StartupMinimize = engine.StartupMinimize;
                    RescheduleOnStartup = engine.RescheduleOnStartup;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "反序列化引擎设置失败"); }
            }

            var appearanceJson = await _settingsService.GetValueAsync("appearance_settings");
            if (!string.IsNullOrEmpty(appearanceJson))
            {
                try
                {
                    var appearance = AppearanceSettings.FromJson(appearanceJson);
                    ThemeMode = appearance.ThemeMode;
                    CompactMode = appearance.CompactMode;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "反序列化外观设置失败"); }
            }

            var formatterSettings = await _formatterService.GetSettingsAsync();
            FormatterTimeoutSeconds = formatterSettings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            BlackFormatterPath = formatterSettings.BlackExecutablePath ?? string.Empty;
            PsscriptAnalyzerModulePath = formatterSettings.PsscriptAnalyzerModulePath ?? string.Empty;
            ShfmtFormatterPath = formatterSettings.ShfmtExecutablePath ?? string.Empty;
            PrettierPackagePath = formatterSettings.PrettierPackagePath ?? string.Empty;

            // 加载时同步系统自启配置，确保与数据库设置一致
            StartupHelper.SyncStartup(StartupEnabled, StartupMinimize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载设置失败");
            ShowToast("加载设置失败", NotificationType.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        if (int.TryParse(tab, out var index))
            SelectedTab = index;
    }

    [RelayCommand]
    private void OpenAddTool()
    {
        NewToolVersion = string.Empty;
        NewToolPath = string.Empty;
        NewToolEnvVariables = string.Empty;
        IsAddToolDialogOpen = true;
    }

    [RelayCommand]
    private void CloseAddTool()
    {
        IsAddToolDialogOpen = false;
        NewToolVersion = string.Empty;
        NewToolPath = string.Empty;
        NewToolEnvVariables = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanAddTool))]
    private async Task AddToolAsync()
    {
        if (!CanAddTool) return;

        var tool = new ToolConfig
        {
            ToolType = NewToolType,
            VersionName = NewToolVersion.Trim(),
            ExecutablePath = NewToolPath.Trim(),
            EnvVariables = string.IsNullOrWhiteSpace(NewToolEnvVariables) ? null : NewToolEnvVariables.Trim()
        };

        await _toolService.AddToolAsync(tool);
        await LoadSettingsAsync();
        CloseAddTool();
    }

    [RelayCommand]
    private async Task BrowseNewToolPathAsync()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow is null)
                return;

            var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "选择可执行文件",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("可执行文件")
                        {
                            Patterns = new[] { "*.exe", "*.bat", "*.cmd", "*.sh", "*.py", "*.ps1" }
                        },
                        new Avalonia.Platform.Storage.FilePickerFileType("所有文件")
                        {
                            Patterns = new[] { "*" }
                        }
                    }
                });

            if (files.Count > 0)
                NewToolPath = files[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选择可执行文件失败");
            ShowToast("选择可执行文件失败", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteToolAsync(ToolConfig? tool)
    {
        if (tool == null) return;
        await _toolService.DeleteToolAsync(tool.Id);
        await LoadSettingsAsync();
    }

    [RelayCommand]
    private async Task SetDefaultToolAsync(ToolConfig? tool)
    {
        if (tool == null) return;
        await _toolService.SetDefaultVersionAsync(tool.Id);
        await LoadSettingsAsync();
    }

    [RelayCommand]
    private async Task SaveEngineSettingsAsync()
    {
        if (!int.TryParse(LogRetentionDays, out var days) || days < 1)
        {
            _logger.LogWarning("无效的日志保留天数: {Value}", LogRetentionDays);
            return;
        }
        if (!int.TryParse(MaxThreads, out var threads) || threads < 1)
        {
            _logger.LogWarning("无效的最大线程数: {Value}", MaxThreads);
            return;
        }
        var engine = new EngineSettings
        {
            LogRetentionDays = days,
            MaxThreads = threads,
            DatabaseType = DatabaseType,
            StartupEnabled = StartupEnabled,
            StartupMinimize = StartupMinimize,
            RescheduleOnStartup = RescheduleOnStartup
        };
        await _settingsService.SetValueAsync("engine_settings", engine.ToJson());
        StartupHelper.SetStartupOnBoot(StartupEnabled, StartupMinimize);
    }

    [RelayCommand]
    private async Task SaveAppearanceSettingsAsync()
    {
        var appearance = new AppearanceSettings
        {
            ThemeMode = ThemeMode,
            CompactMode = CompactMode
        };
        await _settingsService.SetValueAsync("appearance_settings", appearance.ToJson());
        ApplyTheme(ThemeMode);
    }

    [RelayCommand]
    private async Task SaveFormatterSettingsAsync()
    {
        if (!int.TryParse(FormatterTimeoutSeconds, out var timeoutSeconds) || !ScriptFormatterSettings.IsValidTimeout(timeoutSeconds))
        {
            FormatterStatusMessage =
                $"超时秒数必须为 {ScriptFormatterDefaults.MinTimeoutSeconds} 到 {ScriptFormatterDefaults.MaxTimeoutSeconds} 之间的整数。";
            return;
        }

        try
        {
            // 就地修改已加载的设置，而不是 new 一个新实例只复制已知字段：
            // 这样 ScriptFormatterSettings 新增属性时无需同步此处，不会在每次保存时把它静默重置为默认值。
            var settings = await _formatterService.GetSettingsAsync();
            settings.TimeoutSeconds = timeoutSeconds;
            // 路径直接赋界面原始值，空白折叠由 ScriptFormatterSettings.NormalizePaths 统一负责
            // （SaveSettingsAsync 落盘前必定调用），避免同一规则在本文件里再写一份实现。
            settings.BlackExecutablePath = BlackFormatterPath;
            settings.PsscriptAnalyzerModulePath = PsscriptAnalyzerModulePath;
            settings.ShfmtExecutablePath = ShfmtFormatterPath;
            settings.PrettierPackagePath = PrettierPackagePath;

            await _formatterService.SaveSettingsAsync(settings);
            ShowToast("格式化工具设置已保存");
            await CheckFormatterSettingsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存格式化工具设置失败");
            FormatterStatusMessage = $"保存失败：{ex.Message}";
            ShowToast("保存格式化工具设置失败", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CheckFormatterSettingsAsync()
    {
        if (IsCheckingFormatters)
        {
            return;
        }

        IsCheckingFormatters = true;
        FormatterStatusMessage = "正在检测格式化工具…";
        try
        {
            var languages = CommandTypes.FormattableTypes;
            var capabilities = await Task.WhenAll(
                languages.Select(language => _formatterService.GetCapabilityAsync(language)));
            var statuses = capabilities.Select(capability => capability.Available
                    ? $"{capability.Language}: {capability.Tool} {capability.Version}"
                    : $"{capability.Language}: {capability.Reason}");

            FormatterStatusMessage = string.Join(Environment.NewLine, statuses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检测格式化工具失败");
            FormatterStatusMessage = $"检测失败：{ex.Message}";
        }
        finally
        {
            IsCheckingFormatters = false;
        }
    }

    [RelayCommand]
    private async Task SetThemeModeAsync(string mode)
    {
        ThemeMode = mode;
        ApplyTheme(mode);
        var appearance = new AppearanceSettings { ThemeMode = ThemeMode, CompactMode = CompactMode };
        await _settingsService.SetValueAsync("appearance_settings", appearance.ToJson());
    }

    private static void ApplyTheme(string mode)
    {
        if (Avalonia.Application.Current is not { } app) return;
        app.RequestedThemeVariant = mode switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        LogRetentionDays = EngineDefaults.LogRetentionDays.ToString(CultureInfo.InvariantCulture);
        MaxThreads = EngineDefaults.MaxThreads.ToString(CultureInfo.InvariantCulture);
        DatabaseType = EngineDefaults.DatabaseType;
        ThemeMode = AppearanceDefaults.ThemeMode;
        CompactMode = AppearanceDefaults.CompactMode;
        StartupEnabled = EngineDefaults.StartupEnabled;
        StartupMinimize = EngineDefaults.StartupMinimize;
        RescheduleOnStartup = EngineDefaults.RescheduleOnStartup;
        ApplyTheme(ThemeMode);
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (IsSaving) return;
        IsSaving = true;
        try
        {
            await SaveEngineSettingsAsync();
            await SaveAppearanceSettingsAsync();
            ShowToast("保存成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存设置失败");
            ShowToast("保存失败", NotificationType.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void OpenAddToolForGroup(string? toolType)
    {
        if (!string.IsNullOrEmpty(toolType))
            NewToolType = toolType;
        NewToolVersion = string.Empty;
        NewToolPath = string.Empty;
        NewToolEnvVariables = string.Empty;
        IsAddToolDialogOpen = true;
    }

    [RelayCommand]
    private async Task EditToolAsync(string? toolType)
    {
        if (string.IsNullOrEmpty(toolType)) return;

        var (icon, color) = GetToolIcon(toolType);
        EditToolType = toolType;
        EditToolIcon = icon;
        EditToolIconColor = color;

        var versions = await _toolService.GetToolsByTypeAsync(toolType);
        EditingVersions = new AvaloniaList<ToolConfig>(versions.OrderByDescending(t => t.IsDefault));

        IsEditToolDialogOpen = true;
    }

    [RelayCommand]
    private void CloseEditTool() => IsEditToolDialogOpen = false;

    [RelayCommand]
    private async Task EditDeleteVersionAsync(ToolConfig? tool)
    {
        if (tool == null) return;
        await _toolService.DeleteToolAsync(tool.Id);

        var versions = await _toolService.GetToolsByTypeAsync(EditToolType);
        EditingVersions = new AvaloniaList<ToolConfig>(versions.OrderByDescending(t => t.IsDefault));
        await LoadSettingsAsync();
    }

    [RelayCommand]
    private async Task EditSetDefaultAsync(ToolConfig? tool)
    {
        if (tool == null) return;
        await _toolService.SetDefaultVersionAsync(tool.Id);

        var versions = await _toolService.GetToolsByTypeAsync(EditToolType);
        EditingVersions = new AvaloniaList<ToolConfig>(versions.OrderByDescending(t => t.IsDefault));
        await LoadSettingsAsync();
    }

    [RelayCommand]
    private async Task EditSaveVersionAsync(ToolConfig? tool)
    {
        if (tool == null) return;
        if (string.IsNullOrWhiteSpace(tool.VersionName) || string.IsNullOrWhiteSpace(tool.ExecutablePath)) return;

        IsSaving = true;
        try
        {
            await _toolService.UpdateToolAsync(tool);

            var versions = await _toolService.GetToolsByTypeAsync(EditToolType);
            EditingVersions = new AvaloniaList<ToolConfig>(versions.OrderByDescending(t => t.IsDefault));
            await LoadSettingsAsync();
            ShowToast("保存成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存版本失败");
            ShowToast("保存失败", NotificationType.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private AvaloniaList<ToolGroupItem> BuildToolGroups(System.Collections.Generic.IReadOnlyList<ToolConfig> tools)
    {
        var groups = tools
            .GroupBy(t => t.ToolType)
            .Select(g =>
            {
                var (icon, color) = GetToolIcon(g.Key);
                return new ToolGroupItem
                {
                    ToolType = g.Key,
                    Icon = icon,
                    IconColor = color,
                    Versions = new AvaloniaList<ToolConfig>(g.OrderByDescending(t => t.IsDefault)),
                    SetDefaultAction = t => SetDefaultToolAsync(t)
                };
            })
            .ToList();

        return new AvaloniaList<ToolGroupItem>(groups);
    }

    /// <summary>
    /// 迁移旧版散装 key-value 设置到新版 JSON blob 格式
    /// </summary>
    private async Task MigrateOldSettingsAsync()
    {
        var oldKeys = new[] { "log_retention_days", "max_threads", "database_type", "theme_mode", "compact_mode", "startup_settings" };
        var hasOldData = false;
        foreach (var key in oldKeys)
        {
            var val = await _settingsService.GetValueAsync(key);
            if (val != null) { hasOldData = true; break; }
        }
        if (!hasOldData) return;

        _logger.LogInformation("检测到旧版设置数据，开始迁移...");

        var logDays = await _settingsService.GetValueAsync("log_retention_days");
        var threads = await _settingsService.GetValueAsync("max_threads");
        var dbType = await _settingsService.GetValueAsync("database_type");
        var startupJson = await _settingsService.GetValueAsync("startup_settings");

        var engine = new EngineSettings();
        if (logDays != null && int.TryParse(logDays, out var d)) engine.LogRetentionDays = d;
        if (threads != null && int.TryParse(threads, out var t)) engine.MaxThreads = t;
        if (dbType != null) engine.DatabaseType = dbType;
        if (!string.IsNullOrEmpty(startupJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(startupJson);
                if (doc.RootElement.TryGetProperty("enabled", out var enabled))
                    engine.StartupEnabled = enabled.GetBoolean();
                if (doc.RootElement.TryGetProperty("minimize", out var minimize))
                    engine.StartupMinimize = minimize.GetBoolean();
            }
            catch { }
        }
        await _settingsService.SetValueAsync("engine_settings", engine.ToJson());

        var theme = await _settingsService.GetValueAsync("theme_mode");
        var compact = await _settingsService.GetValueAsync("compact_mode");
        var appearance = new AppearanceSettings();
        if (theme != null) appearance.ThemeMode = theme;
        if (compact != null) appearance.CompactMode = compact == "true";
        await _settingsService.SetValueAsync("appearance_settings", appearance.ToJson());

        foreach (var key in oldKeys)
            await _settingsService.DeleteAsync(key);

        _logger.LogInformation("旧版设置数据迁移完成");
    }

    private static (string icon, string color) GetToolIcon(string toolType) => toolType switch
    {
        "Python" => ("Py", "#3776ab"),
        "PowerShell" => ("PS", "#3776ab"),
        "Node.js" => ("JS", "#3776ab"),
        "Shell" or "Shell / Bash" or "Bash" => ("Sh", "#3776ab"),
        "DotNet" or "C#" => ("C#", "#512bd4"),
        "Ruby" => ("Rb", "#3776ab"),
        "Go" => ("Go", "#3776ab"),
        "Java" => ("Jv", "#3776ab"),
        "VBScript" => ("VB", "#3776ab"),
        _ => (toolType.Length >= 2 ? toolType[..2] : toolType, "#3776ab")
    };
}
