namespace TaskScheduler.Core.Models;

/// <summary>
/// 脚本格式化功能的集中默认值与硬性约束，供格式化服务、设置界面与 CLI 共用，避免多处重复硬编码。
/// </summary>
public static class ScriptFormatterDefaults
{
    /// <summary>默认格式化超时（秒）。</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>允许的最小格式化超时（秒）。</summary>
    public const int MinTimeoutSeconds = 1;

    /// <summary>允许的最大格式化超时（秒）。</summary>
    public const int MaxTimeoutSeconds = 120;

    /// <summary>Prettier 的最低支持版本，低于该版本会被判定为不可用。</summary>
    public const string MinimumPrettierVersion = "3.9.6";

    /// <summary>
    /// 超时选项的命令行描述。Attribute 参数必须是编译期常量，无法由
    /// <see cref="MinTimeoutSeconds"/> / <see cref="MaxTimeoutSeconds"/> 拼接得出，
    /// 因此单独定义，并由单元测试保证与两者保持一致。
    /// </summary>
    public const string TimeoutOptionDescription = "格式化超时秒数（1 到 120）";
}
