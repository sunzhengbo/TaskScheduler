using System.Text.RegularExpressions;

namespace TaskScheduler.Infra.Helpers;

public static partial class PathHelper
{
    /// <summary>
    /// 获取系统的数据目录
    /// </summary>
    /// <param name="appName">程序名称</param>
    /// <param name="fragments">父级目录</param>
    /// <returns></returns>
    /// <exception cref="PlatformNotSupportedException"></exception>
    public static string GetOsDataDir(string appName, params string[] fragments)
    {
        if (OperatingSystem.IsWindows())
        {
            var folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dirPath = fragments.Length > 0
                ? Path.Combine(folderPath, string.Join(Path.DirectorySeparatorChar, fragments), appName)
                : Path.Combine(folderPath, appName);
            return dirPath;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Unsupported OS");

        {
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(xdgDataHome))
            {
                xdgDataHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share"
                );
            }

            var dirPath = fragments.Length > 0
                ? Path.Combine(xdgDataHome, string.Join(Path.DirectorySeparatorChar, fragments), appName)
                : Path.Combine(xdgDataHome, appName);
            return dirPath;
        }
    }

    private static readonly HashSet<string> FileComplexExtensions =
    [
        "tar.gz", "tar.bz2", "tar.xz", "tar.br", "tar.lz4", "iso.gz"
    ];

    /// <summary>
    /// 获取文件后缀
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <returns>文件拓展名</returns>
    public static string GetExtension(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var name = Path.GetFileName(fileName);
        var fragments = name.Split(".", StringSplitOptions.RemoveEmptyEntries);

        var fileExtension = Path.GetExtension(name);
        switch (fragments.Length)
        {
            case 1:
                return string.Empty;
            case 2:
                return fileExtension;
        }

        // 适用r01,z01,7z.001,主要针对的是分割压缩的类型
        // 如果是分割压缩，就跳过分割压缩的拓展名，往前进一位，从倒数第二位取
        var index = IsEndsWithTwoDigitsRegex().IsMatch(fileExtension) ? 2 : 1;

        // 判断是否是复合类型
        var complexExt = $"{fragments[^(index + 1)]}.{fragments[^index]}";
        var extension = FileComplexExtensions.Contains(complexExt) ? complexExt : fragments[^index];

        // 根据是否是分割压缩，决定是否加上分割压缩的拓展名
        return index == 1 ? $".{extension}" : $".{extension}{fileExtension}";
    }

    /// <summary>
    /// 获取文件后缀，不带点
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static string GetFileExtensionWithoutDot(string fileName)
    {
        var fileExtension = GetExtension(fileName);
        return fileExtension.StartsWith('.') ? fileExtension.TrimStart('.') : fileExtension;
    }

    /// <summary>
    /// 获取相对路径, 系统那个函数，如果路径相同，返回点
    /// </summary>
    /// <param name="basePath"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string GetRelativePath(string basePath, string path)
    {
        var relativePath = Path.GetRelativePath(basePath, path);
        return relativePath.Equals(".") ? string.Empty : relativePath;
    }

    /// <summary>
    /// 获取文件的基础名称，不包含文件拓展名, 包含路径
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static string GetFullBasename(string fileName)
    {
        return Path.Join(Path.GetDirectoryName(fileName), GetBasename(fileName));
    }

    /// <summary>
    /// 获取文件的基础名称，不包含文件拓展名
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static string GetBasename(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var extension = GetExtension(fileName);
        return string.IsNullOrWhiteSpace(extension) ? name : name.Replace(extension, string.Empty);
    }

    [GeneratedRegex(@"\d{2}$")]
    private static partial Regex IsEndsWithTwoDigitsRegex();
}