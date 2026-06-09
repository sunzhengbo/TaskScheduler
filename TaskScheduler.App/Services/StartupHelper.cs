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
    public static void SetStartupOnBoot(bool enabled)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetWindowsStartup(enabled);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            SetMacStartup(enabled);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SetLinuxStartup(enabled);
    }

    /// <summary>
    /// 同步系统自启配置，使其与期望状态一致
    /// </summary>
    public static void SyncStartup(bool shouldBeEnabled)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SyncWindowsStartup(shouldBeEnabled);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            SyncMacStartup(shouldBeEnabled);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SyncLinuxStartup(shouldBeEnabled);
    }

    #region Windows

    [SupportedOSPlatform("windows")]
    private static void SetWindowsStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(RegistryKey, $"\"{exePath}\"");
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
    private static void SyncWindowsStartup(bool shouldBeEnabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return;

            var currentValue = key.GetValue(RegistryKey)?.ToString();
            var exePath = Environment.ProcessPath ?? string.Empty;
            var expectedValue = string.IsNullOrWhiteSpace(exePath) ? null : $"\"{exePath}\"";
            var isSet = currentValue != null;

            if (shouldBeEnabled && !isSet && expectedValue != null)
                key.SetValue(RegistryKey, expectedValue);
            else if (!shouldBeEnabled && isSet)
                key.DeleteValue(RegistryKey, false);
            else if (shouldBeEnabled && isSet && currentValue != expectedValue && expectedValue != null)
                key.SetValue(RegistryKey, expectedValue);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"同步 Windows 开机自启失败: {ex.Message}");
        }
    }

    #endregion

    #region macOS

    private static string BuildPlistContent()
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
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>KeepAlive</key>
                    <false/>
                </dict>
                </plist>
                """;
    }

    private static void SetMacStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var dir = Path.GetDirectoryName(PlistPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(PlistPath, BuildPlistContent());
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

    private static void SyncMacStartup(bool shouldBeEnabled)
    {
        try
        {
            var exists = File.Exists(PlistPath);
            var exePath = Environment.ProcessPath ?? AppName;

            if (shouldBeEnabled)
            {
                var expectedContent = BuildPlistContent();
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

    private static string BuildDesktopFileContent()
    {
        var exePath = Environment.ProcessPath ?? AppName;
        return $"""
                [Desktop Entry]
                Type=Application
                Name=TaskScheduler
                Comment=Task Scheduler Application
                Exec={exePath}
                Terminal=false
                X-GNOME-Autostart-enabled=true
                """;
    }

    private static void SetLinuxStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var dir = Path.GetDirectoryName(DesktopFilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(DesktopFilePath, BuildDesktopFileContent());
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

    private static void SyncLinuxStartup(bool shouldBeEnabled)
    {
        try
        {
            var exists = File.Exists(DesktopFilePath);
            var expectedContent = BuildDesktopFileContent();

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
