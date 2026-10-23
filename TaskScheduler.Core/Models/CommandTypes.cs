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
    /// 为任意支持的命令类型（包括内置与外部解释器）自动检测系统环境中的可执行文件绝对路径。
    /// 支持系统/用户 PATH、版本管理器（mise, nvm, fnm, volta, scoop）及系统默认安装目录。
    /// </summary>
    public static string? DetectSystemInterpreterPath(string commandType)
    {
        var normalized = Normalize(commandType);

        return normalized switch
        {
            PowerShell => DetectPowerShellPath(),
            Python => DetectPythonPath(),
            NodeJs => DetectNodeJsPath(),
            Shell => DetectShellPath(),
            Cmd => DetectCmdPath(),
            VBScript => DetectVBScriptPath(),
            _ => null
        };
    }

    private static string? DetectPowerShellPath()
    {
        // 1. 优先使用现代跨平台 pwsh
        var pwsh = SearchInPath("pwsh");
        if (pwsh != null) return pwsh;

        // 2. Windows 下检测内置 Windows PowerShell
        if (OperatingSystem.IsWindows())
        {
            var winPs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(winPs)) return winPs;

            var psFromPath = SearchInPath("powershell");
            if (psFromPath != null) return psFromPath;
        }

        return null;
    }

    private static string? DetectPythonPath()
    {
        var py = SearchInPath("python") ?? SearchInPath("python3");
        if (py != null) return py;

        if (OperatingSystem.IsWindows())
        {
            // 检测 mise
            var miseCandidates = GetMiseBaseDirectories();
            foreach (var baseDir in miseCandidates)
            {
                var pyInstalls = Path.Combine(baseDir, "installs", "python");
                if (Directory.Exists(pyInstalls))
                {
                    try
                    {
                        var vDirs = Directory.EnumerateDirectories(pyInstalls)
                            .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
                        foreach (var vDir in vDirs)
                        {
                            var exe = Path.Combine(vDir, "python.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                    catch { }
                }

                var shim = Path.Combine(baseDir, "shims", "python.exe");
                if (File.Exists(shim)) return shim;
            }

            // 检测 Windows 下常见的当前用户 LocalAppData 安装目录
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programsPython = Path.Combine(localAppData, "Programs", "Python");
            if (Directory.Exists(programsPython))
            {
                try
                {
                    var vDirs = Directory.EnumerateDirectories(programsPython)
                        .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
                    foreach (var vDir in vDirs)
                    {
                        var exe = Path.Combine(vDir, "python.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
                catch { }
            }

            // 检测 Scoop
            var scoopDir = GetEnv("SCOOP") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop");
            var scoopPy = Path.Combine(scoopDir, "apps", "python", "current", "python.exe");
            if (File.Exists(scoopPy)) return scoopPy;
        }

        return null;
    }

    private static string? DetectNodeJsPath()
    {
        // 1. 优先从全局/用户/进程 PATH 搜索
        var fromPath = SearchInPath("node");
        if (fromPath != null) return fromPath;

        if (OperatingSystem.IsWindows())
        {
            // 2. 深度探测 mise (包括 MISE_DATA_DIR、用户目录与 Scoop/Local 路径)
            var miseCandidates = GetMiseBaseDirectories();
            foreach (var baseDir in miseCandidates)
            {
                // 2.1 检查 mise 软链接别名 (latest, lts, global)
                var aliases = new[] { "latest", "lts", "global" };
                foreach (var alias in aliases)
                {
                    var aliasExe = Path.Combine(baseDir, "installs", "node", alias, "node.exe");
                    if (File.Exists(aliasExe)) return aliasExe;
                }

                // 2.2 扫描 installs/node 下各版本目录（单层枚举，按版本号倒序取最新）
                var nodeInstalls = Path.Combine(baseDir, "installs", "node");
                if (Directory.Exists(nodeInstalls))
                {
                    try
                    {
                        var vDirs = Directory.EnumerateDirectories(nodeInstalls)
                            .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
                        foreach (var vDir in vDirs)
                        {
                            var exe = Path.Combine(vDir, "node.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                    catch { }
                }

                // 2.3 检查 shims
                var shimExe = Path.Combine(baseDir, "shims", "node.exe");
                if (File.Exists(shimExe)) return shimExe;
            }

            // 3. 探测 NVM (Node Version Manager for Windows)
            var nvmHome = GetEnv("NVM_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvm");
            if (Directory.Exists(nvmHome))
            {
                var nvmSymlink = GetEnv("NVM_SYMLINK");
                if (!string.IsNullOrWhiteSpace(nvmSymlink))
                {
                    var symlinkExe = Path.Combine(nvmSymlink, "node.exe");
                    if (File.Exists(symlinkExe)) return symlinkExe;
                }
                try
                {
                    var vDirs = Directory.EnumerateDirectories(nvmHome)
                        .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
                    foreach (var vDir in vDirs)
                    {
                        var exe = Path.Combine(vDir, "node.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
                catch { }
            }

            // 4. 探测 Scoop
            var scoopDir = GetEnv("SCOOP") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop");
            var scoopCandidates = new[]
            {
                Path.Combine(scoopDir, "apps", "nodejs", "current", "node.exe"),
                Path.Combine(scoopDir, "apps", "nodejs-lts", "current", "node.exe"),
                Path.Combine(scoopDir, "shims", "node.exe")
            };
            foreach (var sp in scoopCandidates)
            {
                if (File.Exists(sp)) return sp;
            }

            // 5. 探测 Volta
            var voltaHome = GetEnv("VOLTA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Volta");
            var voltaNode = Path.Combine(voltaHome, "bin", "node.exe");
            if (File.Exists(voltaNode)) return voltaNode;

            // 6. 探测 fnm
            var fnmDir = GetEnv("FNM_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fnm_multishells");
            if (Directory.Exists(fnmDir))
            {
                try
                {
                    var vDirs = Directory.EnumerateDirectories(fnmDir)
                        .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
                    foreach (var vDir in vDirs)
                    {
                        var exe = Path.Combine(vDir, "node.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
                catch { }
            }

            // 7. 标准系统安装目录
            var standardDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node.exe")
            };
            foreach (var path in standardDirs)
            {
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    private static string? DetectShellPath()
    {
        var bash = SearchInPath("bash") ?? SearchInPath("sh");
        if (bash != null) return bash;

        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "bash.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe")
            };
            foreach (var cand in candidates)
            {
                if (File.Exists(cand)) return cand;
            }
        }

        return null;
    }

    private static string? DetectCmdPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (File.Exists(cmdPath)) return cmdPath;
        return SearchInPath("cmd");
    }

    private static string? DetectVBScriptPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var cscriptPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cscript.exe");
        if (File.Exists(cscriptPath)) return cscriptPath;
        return SearchInPath("cscript");
    }

    private static List<string> GetMiseBaseDirectories()
    {
        var list = new List<string>();
        var miseDataDir = GetEnv("MISE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(miseDataDir))
        {
            list.Add(miseDataDir);
        }
        list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mise"));
        list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "mise"));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
    }

    /// <summary>
    /// 获取环境变量值（依次尝试 Process -> User -> Machine 三个级别）。
    /// </summary>
    public static string? GetEnv(string name)
    {
        var val = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(val)) return val;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                val = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrWhiteSpace(val)) return val;

                val = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
            catch
            {
                // 忽略受限权限环境异常
            }
        }

        return null;
    }

    /// <summary>
    /// 从系统环境变量 PATH（包含进程级、用户级与系统级）中查找指定名称的可执行文件绝对路径。
    /// </summary>
    public static string? SearchInPath(string fileName, IEnumerable<string>? extensions = null)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var allDirs = new List<string>();

        // 收集各级 PATH 变量并去重
        var pathSources = new List<string?> { Environment.GetEnvironmentVariable("PATH") };
        if (OperatingSystem.IsWindows())
        {
            try
            {
                pathSources.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
                pathSources.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            }
            catch { }
        }

        foreach (var pathEnv in pathSources)
        {
            if (string.IsNullOrWhiteSpace(pathEnv)) continue;
            var dirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            allDirs.AddRange(dirs);
        }

        var exts = extensions?.ToList() ?? (OperatingSystem.IsWindows() && !Path.HasExtension(fileName)
            ? new List<string> { ".exe", ".cmd", ".bat", "" }
            : new List<string> { "" });

        foreach (var dir in allDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var trimmed = dir.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            try
            {
                foreach (var ext in exts)
                {
                    var fullPath = Path.Combine(trimmed, fileName + ext);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
            {
                // 跳过无效、畸形或无权限访问的 PATH 条目
            }
        }

        return null;
    }

    /// <summary>
    /// 为内置命令类型（Cmd、VBScript）自动检测系统解释器的可执行文件路径
    /// </summary>
    public static string? DetectBuiltInInterpreterPath(string commandType) => DetectSystemInterpreterPath(commandType);
}
