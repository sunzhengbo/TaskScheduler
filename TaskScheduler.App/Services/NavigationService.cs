using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace TaskScheduler.App.Services;

/// <summary>
/// 导航服务接口
/// </summary>
public interface INavigationService
{
    /// <summary>当前页面 ViewModel</summary>
    ObservableObject? CurrentPage { get; }

    /// <summary>页面切换事件</summary>
    event Action? CurrentPageChanged;

    /// <summary>导航到指定 ViewModel</summary>
    void NavigateTo<TViewModel>() where TViewModel : ObservableObject;

    /// <summary>导航到指定 ViewModel 并传递参数</summary>
    void NavigateTo<TViewModel>(object parameter) where TViewModel : ObservableObject;

    /// <summary>导航到指定 ViewModel 类型</summary>
    void NavigateTo(Type viewModelType);

    /// <summary>返回上一页</summary>
    void GoBack();
}

/// <summary>
/// 导航服务实现
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private ObservableObject? _currentPage;
    private readonly Stack<ObservableObject> _history = new();

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ObservableObject? CurrentPage
    {
        get => _currentPage;
        private set
        {
            _currentPage = value;
            CurrentPageChanged?.Invoke();
        }
    }

    public event Action? CurrentPageChanged;

    public void NavigateTo<TViewModel>() where TViewModel : ObservableObject
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();
        if (_currentPage != null)
            _history.Push(_currentPage);
        CurrentPage = vm;
    }

    public void NavigateTo<TViewModel>(object parameter) where TViewModel : ObservableObject
    {
        var vm = _serviceProvider.GetRequiredService<TViewModel>();

        // 通过 IParameterReceiver 接口传递参数
        if (vm is IParameterReceiver receiver)
        {
            receiver.OnParameterReceived(parameter);
        }

        if (_currentPage != null)
            _history.Push(_currentPage);
        CurrentPage = vm;
    }

    public void NavigateTo(Type viewModelType)
    {
        if (!typeof(ObservableObject).IsAssignableFrom(viewModelType))
        {
            throw new ArgumentException($"Type '{viewModelType.Name}' is not an ObservableObject.", nameof(viewModelType));
        }

        var vm = (ObservableObject)_serviceProvider.GetRequiredService(viewModelType);
        if (_currentPage != null)
            _history.Push(_currentPage);
        CurrentPage = vm;
    }

    public void GoBack()
    {
        if (_history.Count > 0)
        {
            CurrentPage = _history.Pop();
        }
    }
}

/// <summary>
/// ViewModel 参数接收接口
/// </summary>
public interface IParameterReceiver
{
    void OnParameterReceived(object parameter);
}
