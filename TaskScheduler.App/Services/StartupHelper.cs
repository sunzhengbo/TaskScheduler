using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32;

namespace TaskScheduler.App.Services;

/// <summary>
/// 跨平台开机自启辅助类
/// </summary>
public static class StartupHelper
{
    private const string AppName = "TaskScheduler";

    // Windows
    private const string RegistryKey = "TaskScheduler";
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // macOS
    private const string PlistLabel = "com.taskscheduler.agent";
    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{PlistLabel}.plist");

    // Linux
    private static string DesktopFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart", "taskscheduler.desktop");

    /// <summary>
    /// 设置或取消开机自启
    /// </summary>
    public static void SetStartupOnBoot(bool enabled, bool minimize)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetWindowsStartup(enabled, minimize);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            SetMacStartup(enabled, minimize);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SetLinuxStartup(enabled, minimize);
    }

    /// <summary>
    /// 同步系统自启配置，使其与期望状态一致
    /// </summary>
    public static void SyncStartup(bool enabled, bool minimize)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SyncWindowsStartup(enabled, minimize);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            SyncMacStartup(enabled, minimize);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SyncLinuxStartup(enabled, minimize);
    }

    #region Windows

    private static string GetWindowsCommand(bool minimize)
    {
        var exePath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exePath)) return string.Empty;
        return minimize ? $"\"{exePath}\" --minimize" : $"\"{exePath}\"";
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsStartup(bool enabled, bool minimize)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            if (enabled)
            {
                var command = GetWindowsCommand(minimize);
                if (!string.IsNullOrWhiteSpace(command))
                    key.SetValue(RegistryKey, command);
            }
            else
            {
                key.DeleteValue(RegistryKey, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 Windows 开机自启失败: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SyncWindowsStartup(bool shouldBeEnabled, bool minimize)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            var currentValue = key.GetValue(RegistryKey)?.ToString();
            var expectedValue = GetWindowsCommand(minimize);
            if (string.IsNullOrWhiteSpace(expectedValue))
            {
                System.Diagnostics.Debug.WriteLine("同步 Windows 开机自启失败: 无法获取可执行文件路径");
                return;
            }
            var isSet = currentValue != null;

            if (shouldBeEnabled && !isSet)
                key.SetValue(RegistryKey, expectedValue);
            else if (!shouldBeEnabled && isSet)
                key.DeleteValue(RegistryKey, false);
            else if (shouldBeEnabled && isSet && currentValue != expectedValue)
                key.SetValue(RegistryKey, expectedValue);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"同步 Windows 开机自启失败: {ex.Message}");
        }
    }

    #endregion

    #region macOS

    private static string BuildPlistContent(bool minimize)
    {
        var exePath = Environment.ProcessPath ?? AppName;
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{PlistLabel}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{exePath}</string>
                {(minimize ? "                        <string>--minimize</string>" : "")}
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>KeepAlive</key>
                    <false/>
                </dict>
                </plist>
                """;
    }

    private static void SetMacStartup(bool enabled, bool minimize)
    {
        try
        {
            if (enabled)
            {
                var dir = Path.GetDirectoryName(PlistPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(PlistPath, BuildPlistContent(minimize));
            }
            else
            {
                if (File.Exists(PlistPath))
                    File.Delete(PlistPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 macOS 开机自启失败: {ex.Message}");
        }
    }

    private static void SyncMacStartup(bool shouldBeEnabled, bool minimize)
    {
        try
        {
            var exists = File.Exists(PlistPath);

            if (shouldBeEnabled)
            {
                var expectedContent = BuildPlistContent(minimize);
                if (!exists || File.ReadAllText(PlistPath) != expectedContent)
                {
                    var dir = Path.GetDirectoryName(PlistPath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.WriteAllText(PlistPath, expectedContent);
                }
            }
            else if (exists)
            {
                File.Delete(PlistPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"同步 macOS 开机自启失败: {ex.Message}");
        }
    }

    #endregion

    #region Linux

    private static string BuildDesktopFileContent(bool minimize)
    {
        var exePath = Environment.ProcessPath ?? AppName;
        var exec = minimize ? $"{exePath} --minimize" : exePath;
        return $"""
                [Desktop Entry]
                Type=Application
                Name=TaskScheduler
                Comment=Task Scheduler Application
                Exec={exec}
                Terminal=false
                X-GNOME-Autostart-enabled=true
                """;
    }

    private static void SetLinuxStartup(bool enabled, bool minimize)
    {
        try
        {
            if (enabled)
            {
                var dir = Path.GetDirectoryName(DesktopFilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(DesktopFilePath, BuildDesktopFileContent(minimize));
            }
            else
            {
                if (File.Exists(DesktopFilePath))
                    File.Delete(DesktopFilePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置 Linux 开机自启失败: {ex.Message}");
        }
    }

    private static void SyncLinuxStartup(bool shouldBeEnabled, bool minimize)
    {
        try
        {
            var exists = File.Exists(DesktopFilePath);
            var expectedContent = BuildDesktopFileContent(minimize);

            if (shouldBeEnabled)
            {
                if (!exists || File.ReadAllText(DesktopFilePath) != expectedContent)
                {
                    var dir = Path.GetDirectoryName(DesktopFilePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.WriteAllText(DesktopFilePath, expectedContent);
                }
            }
            else if (exists)
            {
                File.Delete(DesktopFilePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"同步 Linux 开机自启失败: {ex.Message}");
        }
    }

    #endregion
}
