namespace TaskScheduler.Core.Models;

/// <summary>
/// 解释器工具配置
/// </summary>
public class ToolConfig
{
    /// <summary>ID</summary>
    public int Id { get; set; }

    /// <summary>工具类型（Python, PowerShell, Node.js, Shell）</summary>
    public string ToolType { get; set; } = string.Empty;

    /// <summary>版本名称（python3.12, pwsh 7.4）</summary>
    public string VersionName { get; set; } = string.Empty;

    /// <summary>可执行文件路径</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>是否为默认版本</summary>
    public bool IsDefault { get; set; }

    /// <summary>环境变量（分号分隔）</summary>
    public string? EnvVariables { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
