namespace TaskScheduler.Core.Models;

/// <summary>
/// 脚本执行模式常量与合法性校验。
/// </summary>
public static class ScriptModes
{
    /// <summary>
    /// 脚本内容模式（内联代码文本）。
    /// </summary>
    public const string Inline = "Inline";

    /// <summary>
    /// 脚本文件模式（物理脚本文件路径）。
    /// </summary>
    public const string File = "File";

    /// <summary>
    /// 检查指定模式字符串是否为受支持的执行模式（忽略大小写）。
    /// </summary>
    public static bool IsValid(string? mode) =>
        string.Equals(mode, Inline, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, File, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 将模式归一化为标准的 PascalCase 格式；若非法则抛出 <see cref="ArgumentException"/>。
    /// </summary>
    public static string Normalize(string? mode)
    {
        if (string.Equals(mode, File, StringComparison.OrdinalIgnoreCase)) return File;
        if (string.Equals(mode, Inline, StringComparison.OrdinalIgnoreCase)) return Inline;
        throw new ArgumentException($"无效的脚本执行模式: '{mode}'，仅支持 '{Inline}' 或 '{File}'。");
    }

    /// <summary>
    /// 针对存量未指定模式的任务或导入文件，根据命令类型与内容推断执行模式。
    /// 若为单行且物理文件存在，或为 C# 且以 .cs 结尾（无论文件当前是否已生成），统一推断为 <see cref="File"/>；其余推断为 <see cref="Inline"/>。
    /// </summary>
    public static string InferLegacyMode(string? commandType, string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return Inline;
        var trimmed = content.Trim();
        bool isSingleLine = !trimmed.Contains('\n') && !trimmed.Contains('\r');
        if (!isSingleLine) return Inline;

        var normalizedType = CommandTypes.Normalize(commandType ?? string.Empty);
        if (System.IO.File.Exists(trimmed) ||
            (normalizedType == CommandTypes.CSharp && trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            return File;
        }

        return Inline;
    }

    /// <summary>
    /// 从文件模式的输入命令行中安全分离出脚本路径与附加参数。
    /// 自动处理引号包裹路径、未加引号但路径含空格的前向探测等场景，并将脚本路径归一化为绝对路径。
    /// </summary>
    public static (string Path, string? Args) SplitScriptPathAndArgs(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (System.IO.File.Exists(trimmed))
            return (SafeGetFullPath(trimmed), null);

        // 1. 引号包裹路径: "C:\My Path\script.cs" arg1 arg2
        if (trimmed.StartsWith('"'))
        {
            var nextQuote = trimmed.IndexOf('"', 1);
            if (nextQuote > 1)
            {
                var quotedPath = trimmed[1..nextQuote].Trim();
                var args = trimmed[(nextQuote + 1)..].Trim();
                return (SafeGetFullPath(quotedPath), string.IsNullOrEmpty(args) ? null : args);
            }
        }

        // 2. 尝试向前探测空格分割点，优先匹配真实存在的本地文件
        // 兼容未加引号且路径包含空格的脚本: C:\Program Files\scripts\task.cs arg1 arg2
        int spaceIndex = trimmed.IndexOf(' ');
        while (spaceIndex > 0)
        {
            var candidatePath = trimmed[..spaceIndex].Trim();
            if (System.IO.File.Exists(candidatePath))
            {
                var candidateArgs = trimmed[(spaceIndex + 1)..].Trim();
                return (SafeGetFullPath(candidatePath), string.IsNullOrEmpty(candidateArgs) ? null : candidateArgs);
            }
            spaceIndex = trimmed.IndexOf(' ', spaceIndex + 1);
        }

        // 3. 兜底按第一个空格切分
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace > 0)
        {
            var pathPart = trimmed[..firstSpace].Trim();
            var args = trimmed[(firstSpace + 1)..].Trim();
            return (SafeGetFullPath(pathPart), string.IsNullOrEmpty(args) ? null : args);
        }

        return (SafeGetFullPath(trimmed), null);
    }

    /// <summary>
    /// 在文件执行模式下归一化脚本路径与参数：将脚本路径提取并归一化为绝对路径，同时原样保留附带的参数部分。
    /// </summary>
    public static string NormalizeScriptCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;
        var (path, args) = SplitScriptPathAndArgs(commandLine);
        if (string.IsNullOrWhiteSpace(args))
        {
            return path;
        }

        var formattedPath = path.Contains(' ') && !path.StartsWith('"')
            ? $"\"{path}\""
            : path;
        return $"{formattedPath} {args}";
    }

    private static string SafeGetFullPath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
