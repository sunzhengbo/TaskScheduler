using Spectre.Console.Cli;

namespace TaskScheduler.Cli.Infrastructure;

/// <summary>
/// 严格命令参数拦截器：检测并拦截任何未定义的未知选项与多余参数，防止拼写错误被静默忽略。
/// </summary>
public sealed class StrictCommandInterceptor : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (context.Remaining.Parsed.Count > 0)
        {
            var unknownOptions = context.Remaining.Parsed
                .Select(p =>
                {
                    var clean = p.Key.TrimStart('-');
                    return clean.Length == 1 ? $"-{clean}" : $"--{clean}";
                })
                .Distinct()
                .ToList();
            throw new ArgumentException($"未知选项: {string.Join(", ", unknownOptions)}。请检查参数拼写或使用 --help 查看命令支持的选项。");
        }

        if (context.Remaining.Raw.Count > 0)
        {
            throw new ArgumentException($"包含多余的未知参数: {string.Join(", ", context.Remaining.Raw)}。请使用 --help 查看命令用法。");
        }
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result)
    {
    }
}
