namespace TaskScheduler.Infra.Helpers;

public static class DirectoryHelper
{
    /// <summary>
    /// 创建目录
    /// </summary>
    /// <param name="path"></param>
    /// <param name="recursive"></param>
    public static void Delete(string path, bool recursive = true)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive);
    }

    /// <summary>
    /// 创建父目录
    /// </summary>
    /// <param name="path"></param>
    public static string CreateParentDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        Directory.CreateDirectory(parent);
        return parent;
    }

    /// <summary>
    /// 创建同名的目录
    /// </summary>
    /// <param name="path"></param>
    public static string CreateSaveNameDirectory(string path)
    {
        var basename = PathHelper.GetFullBasename(path);
        var parent = Path.GetDirectoryName(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        var name = Path.Combine(parent, basename);
        Directory.CreateDirectory(name);
        return name;
    }
}