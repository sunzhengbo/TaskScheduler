using System.Text.RegularExpressions;

namespace TaskScheduler.Core.Detectors;

/// <summary>
/// 辅助版本管理工具目录按真实语义化版本倒序排列，避免字典序错误（如 "3.9" 误排在 "3.10" 前面）。
/// </summary>
public static partial class VersionOrderHelper
{
    [GeneratedRegex(@"\bv?(\d+\.\d+(?:\.\d+)*)\b")]
    private static partial Regex DotVersionRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex GenericNumberRegex();

    /// <summary>
    /// 对包含版本号的目录或字符串集合，按提取的语义版本倒序排列（最新版本优先）。
    /// </summary>
    public static IEnumerable<string> OrderByDescendingVersion(this IEnumerable<string> items)
    {
        return items.OrderByDescending(item =>
        {
            var fileName = Path.GetFileName(item);
            var dotMatch = DotVersionRegex().Match(fileName);
            var match = dotMatch.Success ? dotMatch : GenericNumberRegex().Match(fileName);
            if (match.Success)
            {
                var verStr = match.Groups[1].Value;
                if (!verStr.Contains('.'))
                {
                    verStr += ".0";
                }
                var parts = verStr.Split('.');
                if (parts.Length > 4)
                {
                    verStr = string.Join('.', parts.Take(4));
                }
                if (Version.TryParse(verStr, out var parsedVersion))
                {
                    return parsedVersion;
                }
            }
            return new Version(0, 0);
        }).ThenByDescending(item => item, StringComparer.OrdinalIgnoreCase);
    }
}
