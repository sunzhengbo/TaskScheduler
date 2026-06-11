using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using TaskScheduler.App.Models;
using TaskScheduler.App.Services;
using TaskScheduler.Core.Models;
using TaskScheduler.Core.Services;

namespace TaskScheduler.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IToolConfigService _toolService;
    private readonly ISettingsService _settingsService;
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
    [ObservableProperty] private bool _startupEnabled;
    [ObservableProperty] private bool _startupMinimize;

    partial void OnStartupEnabledChanged(bool value)
    {
        if (!value)
            StartupMinimize = false;
    }
    [ObservableProperty] private string _logRetentionDays = "30";
    [ObservableProperty] private string _maxThreads = "10";
    [ObservableProperty] private string _databaseType = "SQLite";

    // 外观设置
    [ObservableProperty] private string _themeMode = "System";
    [ObservableProperty] private bool _compactMode;

    // 添加工具对话框
    [ObservableProperty] private bool _isAddToolDialogOpen;
    [ObservableProperty] private string _newToolType = "Python";
    [ObservableProperty] private string _newToolVersion = string.Empty;
    [ObservableProperty] private string _newToolPath = string.Empty;
    [ObservableProperty] private string _newToolEnvVariables = string.Empty;

    // 编辑工具对话框
    [ObservableProperty] private bool _isEditToolDialogOpen;
    [ObservableProperty] private string _editToolType = string.Empty;
    [ObservableProperty] private string _editToolIcon = string.Empty;
    [ObservableProperty] private string _editToolIconColor = "#3776ab";
    [ObservableProperty] private AvaloniaList<ToolConfig> _editingVersions = new();

    public AvaloniaList<string> ToolTypes { get; } = ["Python", "PowerShell", "Node.js", "Shell"];
    public AvaloniaList<string> DatabaseTypes { get; } = ["SQLite"];
    public AvaloniaList<string> ThemeModes { get; } = ["System", "Light", "Dark"];

    public SettingsViewModel(IToolConfigService toolService, ISettingsService settingsService, ILogger<SettingsViewModel> logger)
    {
        _toolService = toolService;
        _settingsService = settingsService;
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
                    LogRetentionDays = engine.LogRetentionDays.ToString();
                    MaxThreads = engine.MaxThreads.ToString();
                    DatabaseType = engine.DatabaseType;
                    StartupEnabled = engine.StartupEnabled;
                    StartupMinimize = engine.StartupMinimize;
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
        NewToolEnvVariables = string.Empty;
        IsAddToolDialogOpen = true;
    }

    [RelayCommand]
    private void CloseAddTool() => IsAddToolDialogOpen = false;

    [RelayCommand]
    private async Task AddToolAsync()
    {
        if (string.IsNullOrWhiteSpace(NewToolVersion) || string.IsNullOrWhiteSpace(NewToolPath)) return;

        var tool = new ToolConfig
        {
            ToolType = NewToolType,
            VersionName = NewToolVersion,
            ExecutablePath = NewToolPath,
            EnvVariables = string.IsNullOrWhiteSpace(NewToolEnvVariables) ? null : NewToolEnvVariables
        };

        await _toolService.AddToolAsync(tool);
        await LoadSettingsAsync();
        IsAddToolDialogOpen = false;
        NewToolVersion = string.Empty;
        NewToolPath = string.Empty;
        NewToolEnvVariables = string.Empty;
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
            StartupMinimize = StartupMinimize
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
        LogRetentionDays = "30";
        MaxThreads = "10";
        DatabaseType = "SQLite";
        ThemeMode = "System";
        CompactMode = false;
        StartupEnabled = false;
        StartupMinimize = false;
        ApplyTheme("System");
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
            ShowToast("保存成功", NotificationType.Success);
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
            ShowToast("保存成功", NotificationType.Success);
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
        "PowerShell" => ("PS", "#012456"),
        "Node.js" => ("JS", "#339933"),
        "Shell" or "Shell / Bash" => ("Sh", "#4eaa25"),
        "Ruby" => ("Rb", "#cc342d"),
        "Go" => ("Go", "#00add8"),
        "Java" => ("Jv", "#ed8b00"),
        _ => (toolType.Length >= 2 ? toolType[..2] : toolType, "#6b6b6b")
    };
}
