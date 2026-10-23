namespace TaskScheduler.Core.Models;

/// <summary>
/// 支持的脚本与命令类型定义及工具映射辅助方法
/// </summary>
public static class CommandTypes
{
    public const string Cmd = "命令提示符 (cmd)";
    public const string PowerShell = "PowerShell";
    public const string Python = "Python";
    public const string Shell = "Bash";
    public const string NodeJs = "Node.js";
    public const string VBScript = "VBScript";

    /// <summary>
    /// 获取当前操作系统支持的全部命令类型列表
    /// </summary>
    public static IReadOnlyList<string> GetAvailableCommandTypes()
    {
        var list = new List<string> { Python, PowerShell, NodeJs, Shell };
        if (OperatingSystem.IsWindows())
        {
            list.Add(Cmd);
            list.Add(VBScript);
        }
        return list;
    }

    /// <summary>
    /// 将用户输入（包括简写别名）规范化为标准 CommandTypes 常量
    /// </summary>
    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return PowerShell;

        return type.Trim().ToLowerInvariant() switch
        {
            "powershell" or "pwsh" or "ps" or "ps1" => PowerShell,
            "python" or "py" or "python3" or "python 脚本" => Python,
            "node.js" or "nodejs" or "node" or "js" or "node.js 脚本" => NodeJs,
            "bash" or "sh" or "shell" or "shell 脚本" => Shell,
            "cmd" or "bat" or "cmd.exe" or "命令提示符 (cmd)" or "命令提示符" => Cmd,
            "vbscript" or "vbs" or "cscript" => VBScript,
            _ => type.Trim()
        };
    }

    /// <summary>
    /// 将命令类型映射为 ToolConfig 中的工具类型名称（内置类型返回 null）
    /// </summary>
    public static string? MapToToolType(string commandType) => commandType switch
    {
        Python or "Python 脚本" => "Python",
        PowerShell or "PowerShell" => "PowerShell",
        NodeJs or "Node.js 脚本" => "Node.js",
        Shell or "Shell 脚本" or "Shell" => "Bash",
        _ => null // Cmd、VBScript 等内置类型无对应 ToolConfig
    };

    /// <summary>
    /// 为内置命令类型（Cmd、VBScript）自动检测系统解释器的可执行文件路径
    /// </summary>
    public static string? DetectBuiltInInterpreterPath(string commandType)
    {
        var exeName = commandType switch
        {
            Cmd => "cmd.exe",
            VBScript => "cscript.exe",
            _ => null
        };
        if (exeName == null) return null;

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            var trimmed = dir.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            try
            {
                var fullPath = Path.Combine(trimmed, exeName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch (IOException)
            {
                // 跳过无效的 PATH 条目
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exeName);
            if (File.Exists(systemPath))
                return systemPath;
        }

        return null;
    }
}
