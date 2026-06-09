using System;
using System.Collections.Generic;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

using Avalonia.Controls.Notifications;

using TaskScheduler.App.Services;

namespace TaskScheduler.App.ViewModels;

/// <summary>
/// 侧边栏导航项
/// </summary>
public partial class NavItem : ObservableObject
{
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public Type TargetViewModelType { get; init; } = null!;
    public bool IsSeparator { get; init; }
    public string? SectionHeader { get; init; }
    public bool IsSection => !string.IsNullOrEmpty(SectionHeader);
}

/// <summary>
/// 主窗口 ViewModel（Shell 布局）
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    internal const double SidebarCollapseThreshold = 900;
    internal const double SidebarExpandedWidth = 260;
    internal const double SidebarCollapsedWidth = 48;

    private readonly INavigationService _navigation;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dictionary<Type, int> _viewModelTypeToIndex = new();

    [ObservableProperty]
    private ObservableObject? _currentPageViewModel;

    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private string _pageTitle = "仪表盘";

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private double _windowWidth = 1280;

    [ObservableProperty]
    private bool _showCreateTaskButton;

    [ObservableProperty]
    private bool _showEditorToolbar;

    [ObservableProperty]
    private TaskEditorViewModel? _activeEditorViewModel;

    [ObservableProperty]
    private bool _showSettingsToolbar;

    [ObservableProperty]
    private SettingsViewModel? _settingsViewModel;

    public AvaloniaList<NavItem> NavItems { get; }

    public MainWindowViewModel(INavigationService navigation, ILogger<MainWindowViewModel> logger)
    {
        _navigation = navigation;
        _logger = logger;
        _navigation.CurrentPageChanged += OnCurrentPageChanged;

        NavItems =
        [
            new NavItem { Title = "仪表盘", Icon = "ViewDashboardOutline", TargetViewModelType = typeof(DashboardViewModel) },
            new NavItem { Title = "新建任务", Icon = "Plus", TargetViewModelType = typeof(TaskEditorViewModel) },
            new NavItem { Title = "任务列表", Icon = "FormatListBulleted", TargetViewModelType = typeof(TaskListViewModel) },
            new NavItem { Title = "执行日志", Icon = "ConsoleLine", TargetViewModelType = typeof(ExecutionLogViewModel) },
            new NavItem { IsSeparator = true, SectionHeader = "系统" },
            new NavItem { Title = "全局设置", Icon = "CogOutline", TargetViewModelType = typeof(SettingsViewModel) },
            new NavItem { Title = "关于", Icon = "InformationOutline", TargetViewModelType = typeof(AboutViewModel) },
        ];

        BuildViewModelTypeIndex();
        _navigation.NavigateTo<DashboardViewModel>();
    }


    private void BuildViewModelTypeIndex()
    {
        for (var i = 0; i < NavItems.Count; i++)
        {
            if (!NavItems[i].IsSeparator)
            {
                _viewModelTypeToIndex[NavItems[i].TargetViewModelType] = i;
            }
        }
    }

    private bool _isNavigatingProgrammatically;

    [RelayCommand]
    private void Navigate(int index)
    {
        if (index < 0 || index >= NavItems.Count) return;
        var item = NavItems[index];
        if (item.IsSeparator) return;

        _isNavigatingProgrammatically = true;
        try
        {
            SelectedNavIndex = index;
        }
        finally
        {
            _isNavigatingProgrammatically = false;
        }

        PageTitle = item.Title;

        try
        {
            _logger.LogDebug("导航到 {Title} (类型: {Type})", item.Title, item.TargetViewModelType.Name);
            _navigation.NavigateTo(item.TargetViewModelType);
            _logger.LogDebug("导航成功，CurrentPage 类型: {Type}", _navigation.CurrentPage?.GetType().Name ?? "null");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到 {Title} (索引 {Index}) 失败", item.Title, index);
            ShowToast($"导航到 \"{item.Title}\" 失败", NotificationType.Error);
            SelectedNavIndex = -1;
        }
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        // 避免 Navigate 内部设置 SelectedNavIndex 时重复触发
        if (_isNavigatingProgrammatically) return;

        if (value >= 0 && value < NavItems.Count && !NavItems[value].IsSeparator)
        {
            Navigate(value);
        }
    }

    [RelayCommand]
    private void NavigateToPage(Type viewModelType)
    {
        if (_viewModelTypeToIndex.TryGetValue(viewModelType, out var index))
        {
            Navigate(index);
        }
    }

    [RelayCommand]
    private void CreateNewTask()
    {
        NavigateToPage(typeof(TaskEditorViewModel));
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    partial void OnWindowWidthChanged(double value)
    {
        IsSidebarCollapsed = value < SidebarCollapseThreshold;
    }

    private void OnCurrentPageChanged()
    {
        _logger.LogDebug("OnCurrentPageChanged: {Type}", _navigation.CurrentPage?.GetType().Name ?? "null");
        CurrentPageViewModel = _navigation.CurrentPage;
        ShowCreateTaskButton = _navigation.CurrentPage is TaskListViewModel;
        ShowEditorToolbar = _navigation.CurrentPage is TaskEditorViewModel;
        ActiveEditorViewModel = _navigation.CurrentPage as TaskEditorViewModel;
        ShowSettingsToolbar = _navigation.CurrentPage is SettingsViewModel;
        SettingsViewModel = _navigation.CurrentPage as SettingsViewModel;

        // 同步侧边栏选中状态（仅同步选中索引，不覆盖 PageTitle）
        SyncSidebarSelection();

        // 根据页面类型设置标题
        if (_navigation.CurrentPage is TaskEditorViewModel editorVm)
        {
            PageTitle = editorVm.PageTitle;
        }
        else
        {
            UpdatePageTitleFromCurrentPage();
        }
    }

    /// <summary>根据当前页面类型同步侧边栏选中项</summary>
    private void SyncSidebarSelection()
    {
        var currentPageType = _navigation.CurrentPage?.GetType();
        if (currentPageType == null) return;

        if (_viewModelTypeToIndex.TryGetValue(currentPageType, out var index))
        {
            _isNavigatingProgrammatically = true;
            try
            {
                if (SelectedNavIndex != index)
                    SelectedNavIndex = index;
            }
            finally
            {
                _isNavigatingProgrammatically = false;
            }
        }
    }

    /// <summary>根据当前页面类型更新标题</summary>
    private void UpdatePageTitleFromCurrentPage()
    {
        var currentPageType = _navigation.CurrentPage?.GetType();
        if (currentPageType == null) return;

        if (_viewModelTypeToIndex.TryGetValue(currentPageType, out var index))
        {
            var item = NavItems[index];
            if (PageTitle != item.Title)
                PageTitle = item.Title;
        }
    }
}
