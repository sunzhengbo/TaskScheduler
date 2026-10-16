using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Messaging;
using Ursa.Controls;
using TaskScheduler.App.Models;
using TaskScheduler.App.ViewModels;

namespace TaskScheduler.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _isShuttingDown;
    private bool _isAdjustingForScreen;
    private double _defaultMinWidth;
    private double _defaultMinHeight;
    private PixelRect? _lastWorkingArea;
    private double? _lastScreenScaling;

    public MainWindow()
    {
        InitializeComponent();
        _defaultMinWidth = MinWidth;
        _defaultMinHeight = MinHeight;

        WeakReferenceMessenger.Default.Register<TokenMessages.ShowWindowMessage>(this,
            (_, _) => { ShowWindowFromTray(); });
    }

    /// <summary>
    /// 标记应用正在退出，使 OnClosing 不再拦截关闭事件
    /// </summary>
    public void SetShuttingDown() => _isShuttingDown = true;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _ = new WindowToastManager(this); // 注册到窗口以支持 Toast 通知

        if (DataContext is MainWindowViewModel vm)
        {
            _viewModel = vm;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateSidebarVisualState(_viewModel.IsSidebarCollapsed);
        }

        PositionChanged += OnPositionChanged;
        ScalingChanged += OnScalingChanged;
        Screens.Changed += OnScreensChanged;
        FitToScreen(center: true);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        PositionChanged -= OnPositionChanged;
        ScalingChanged -= OnScalingChanged;
        Screens.Changed -= OnScreensChanged;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        base.OnUnloaded(e);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarCollapsed) && _viewModel != null)
        {
            UpdateSidebarVisualState(_viewModel.IsSidebarCollapsed);
        }
    }

    private void UpdateSidebarVisualState(bool isCollapsed)
    {
        if (SidebarBorder != null && BrandText != null)
        {
            SidebarBorder.Width = isCollapsed
                ? MainWindowViewModel.SidebarCollapsedWidth
                : MainWindowViewModel.SidebarExpandedWidth;
            BrandText.IsVisible = !isCollapsed;
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        FitToScreen(position: e.Point);
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        FitToScreen();
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        FitToScreen();
    }

    private void FitToScreen(bool center = false, PixelPoint? position = null)
    {
        if (_isAdjustingForScreen)
        {
            return;
        }

        var screen = position is { } point
            ? Screens.ScreenFromPoint(point)
            : Screens.ScreenFromWindow(this);
        screen ??= Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;
        if (!center && _lastWorkingArea == workArea && _lastScreenScaling == scaling)
        {
            return;
        }

        _lastWorkingArea = workArea;
        _lastScreenScaling = scaling;
        var maxWidth = workArea.Width / scaling * 0.96;
        var maxHeight = workArea.Height / scaling * 0.96;

        _isAdjustingForScreen = true;
        try
        {
            MinWidth = Math.Min(_defaultMinWidth, maxWidth);
            MinHeight = Math.Min(_defaultMinHeight, maxHeight);
            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);

            var widthInPixels = (int)Math.Ceiling(Width * scaling);
            var heightInPixels = (int)Math.Ceiling(Height * scaling);
            var maxX = workArea.X + workArea.Width - widthInPixels;
            var maxY = workArea.Y + workArea.Height - heightInPixels;
            var currentPosition = position ?? Position;
            Position = center
                ? new PixelPoint(
                    workArea.X + (workArea.Width - widthInPixels) / 2,
                    workArea.Y + (workArea.Height - heightInPixels) / 2)
                : new PixelPoint(
                    Math.Clamp(currentPosition.X, workArea.X, maxX),
                    Math.Clamp(currentPosition.Y, workArea.Y, maxY));
        }
        finally
        {
            _isAdjustingForScreen = false;
        }
    }

    private void ShowWindowFromTray()
    {
        // 最小化启动时 ShutdownMode 被设为 OnExplicitShutdown（见 App.axaml.cs），
        // 窗口关闭不会退出应用（托盘常驻行为），此处仅恢复 MainWindow 引用。
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow == null)
        {
            desktop.MainWindow = this;
        }
        Show();
        Activate();
        WindowState = WindowState.Normal;
    }

    private void Window_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.WindowWidth = e.NewSize.Width;
        }
    }
}
