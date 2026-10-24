using TaskScheduler.Core.Detectors;

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
    public const string CSharp = "C# (.NET)";

    /// <summary>
    /// 获取当前操作系统支持的全部命令类型列表
    /// </summary>
    public static IReadOnlyList<string> GetAvailableCommandTypes()
    {
        var list = new List<string> { Python, PowerShell, NodeJs, Shell, CSharp };
        if (OperatingSystem.IsWindows())
        {
            list.Add(Cmd);
            list.Add(VBScript);
        }
        return list;
    }

    /// <summary>
    /// 获取当前真正可用的命令类型列表：
    /// 仅包含已在全局工具库中配置，或在本机环境变量/系统路径中检测到有效解释器的类型。
    /// （若传入了 currentType，则始终保留当前类型以防历史任务数据丢失）。
    /// </summary>
    public static IReadOnlyList<string> GetConfiguredOrAvailableCommandTypes(
        IReadOnlyList<ToolConfig>? configuredTools = null,
        string? currentType = null)
    {
        var allCandidates = GetAvailableCommandTypes();
        var result = new List<string>();

        foreach (var type in allCandidates)
        {
            // 1. 如果全局工具库中配置了该类型
            var toolType = MapToToolType(type);
            var isConfiguredInTools = toolType != null &&
                                      configuredTools != null &&
                                      configuredTools.Any(t => t.ToolType.Equals(toolType, StringComparison.OrdinalIgnoreCase));

            // 2. 如果本机环境变量或系统路径中能够检测到有效可执行文件
            var detectedPath = DetectSystemInterpreterPath(type);
            var isAvailableInSystem = !string.IsNullOrWhiteSpace(detectedPath);

            // 3. 兼容已有任务的类型（防丢失）
            var isCurrent = !string.IsNullOrWhiteSpace(currentType) &&
                            string.Equals(Normalize(type), Normalize(currentType), StringComparison.OrdinalIgnoreCase);

            if (isConfiguredInTools || isAvailableInSystem || isCurrent)
            {
                result.Add(type);
            }
        }

        // 3. 若传入了已有任务的类型且不在候选标准类型中，确保其被保留，防止老任务数据丢失
        if (!string.IsNullOrWhiteSpace(currentType) &&
            !result.Any(r => string.Equals(Normalize(r), Normalize(currentType), StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(currentType);
        }

        // 兜底保底，防止极端情况下列表为空
        if (result.Count == 0)
        {
            result.Add(OperatingSystem.IsWindows() ? PowerShell : Shell);
        }

        return result;
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
            "c#" or "cs" or "csharp" or "dotnet" or ".net" or "c# (.net)" or "c# 脚本" => CSharp,
            _ => type.Trim()
        };
    }

    /// <summary>
    /// 将命令类型映射为 ToolConfig 中的工具类型名称（内置类型返回 null）
    /// </summary>
    public static string? MapToToolType(string? commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType)) return null;
        var normalized = Normalize(commandType);
        return normalized switch
        {
            Python => "Python",
            PowerShell => "PowerShell",
            NodeJs => "Node.js",
            Shell => "Bash",
            CSharp => "DotNet",
            _ => null // Cmd、VBScript 等内置类型无对应 ToolConfig
        };
    }

    /// <summary>
    /// 为任意支持的命令类型（包括内置与外部解释器）自动检测系统环境中的可执行文件绝对路径。
    /// 静态外观方法，过渡兼容旧调用，内部委托给 InterpreterDetectionService.Default。
    /// </summary>
    public static string? DetectSystemInterpreterPath(string commandType)
    {
        var normalized = Normalize(commandType);
        return InterpreterDetectionService.Default.DetectAsync(normalized).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 为内置命令类型（Cmd、VBScript）自动检测系统解释器的可执行文件路径
    /// </summary>
    public static string? DetectBuiltInInterpreterPath(string commandType) => DetectSystemInterpreterPath(commandType);
}
