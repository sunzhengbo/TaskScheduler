using System;
using System.Linq;
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
    [ObservableProperty] private string _saveStatusMessage = string.Empty;

    public string SaveButtonText => IsSaving ? "保存中..." : "保存设置";

    // 引擎设置
    [ObservableProperty] private bool _isStartupOnBootEnabled;
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

            var logDays = await _settingsService.GetValueAsync("log_retention_days");
            if (logDays != null) LogRetentionDays = logDays;

            var threads = await _settingsService.GetValueAsync("max_threads");
            if (threads != null) MaxThreads = threads;

            var dbType = await _settingsService.GetValueAsync("database_type");
            if (dbType != null) DatabaseType = dbType;

            var theme = await _settingsService.GetValueAsync("theme_mode");
            if (theme != null) ThemeMode = theme;

            var compact = await _settingsService.GetValueAsync("compact_mode");
            if (compact != null) CompactMode = compact == "true";

            var startup = await _settingsService.GetValueAsync("startup_on_boot");
            IsStartupOnBootEnabled = startup == "true";

            // 加载时同步系统自启配置，确保与数据库设置一致
            StartupHelper.SyncStartup(IsStartupOnBootEnabled);
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
        await _settingsService.SetValueAsync("log_retention_days", days.ToString());
        await _settingsService.SetValueAsync("max_threads", threads.ToString());
        await _settingsService.SetValueAsync("database_type", DatabaseType);

        await _settingsService.SetValueAsync("startup_on_boot", IsStartupOnBootEnabled.ToString().ToLower());
        StartupHelper.SetStartupOnBoot(IsStartupOnBootEnabled);
    }

    [RelayCommand]
    private async Task SaveAppearanceSettingsAsync()
    {
        await _settingsService.SetValueAsync("theme_mode", ThemeMode);
        await _settingsService.SetValueAsync("compact_mode", CompactMode.ToString().ToLower());
        ApplyTheme(ThemeMode);
    }

    [RelayCommand]
    private async Task SetThemeModeAsync(string mode)
    {
        ThemeMode = mode;
        ApplyTheme(mode);
        await _settingsService.SetValueAsync("theme_mode", mode);
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
        ApplyTheme("System");
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (IsSaving) return;
        IsSaving = true;
        SaveStatusMessage = string.Empty;
        try
        {
            await SaveEngineSettingsAsync();
            await SaveAppearanceSettingsAsync();
            ShowToast("保存成功", NotificationType.Success);
            SaveStatusMessage = "✓ 保存成功";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存设置失败");
            ShowToast("保存失败", NotificationType.Error);
            SaveStatusMessage = "✗ 保存失败";
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
        SaveStatusMessage = string.Empty;
        try
        {
            await _toolService.UpdateToolAsync(tool);

            var versions = await _toolService.GetToolsByTypeAsync(EditToolType);
            EditingVersions = new AvaloniaList<ToolConfig>(versions.OrderByDescending(t => t.IsDefault));
            await LoadSettingsAsync();
            ShowToast("保存成功", NotificationType.Success);
            SaveStatusMessage = "✓ 保存成功";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存版本失败");
            ShowToast("保存失败", NotificationType.Error);
            SaveStatusMessage = "✗ 保存失败";
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
