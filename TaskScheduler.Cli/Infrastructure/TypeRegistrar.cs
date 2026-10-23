using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using TaskScheduler.Core;

namespace TaskScheduler.Cli.Infrastructure;

/// <summary>
/// 将 Microsoft.Extensions.DependencyInjection 桥接到 Spectre.Console.Cli
/// </summary>
public sealed class TypeRegistrar(IServiceCollection builder) : ITypeRegistrar
{
    private IServiceProvider? _serviceProvider;
    private bool _dbInitialized;

    public ITypeResolver Build()
    {
        _serviceProvider ??= builder.BuildServiceProvider();
        if (!_dbInitialized)
        {
            _serviceProvider.InitDb();
            _dbInitialized = true;
        }
        return new TypeResolver(_serviceProvider);
    }

    public void Register(Type service, Type implementation)
    {
        builder.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        builder.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        builder.AddSingleton(service, _ => func());
    }
}
