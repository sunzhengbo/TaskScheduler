using Spectre.Console.Cli;

namespace TaskScheduler.Cli.Infrastructure;

/// <summary>
/// 基于 IServiceProvider 的 Spectre.Console.Cli 类型解析器
/// </summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public object? Resolve(Type? type)
    {
        return type == null ? null : _provider.GetService(type);
    }

    public void Dispose()
    {
        if (_provider is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
