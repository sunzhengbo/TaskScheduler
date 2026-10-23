using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskScheduler.Infra.Helpers;

/// <summary>
/// 跨平台剪贴板读写辅助类（无 GUI 框架依赖，支持 CLI 场景）
/// </summary>
public static class ClipboardHelper
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>
    /// 将文本写入系统剪贴板
    /// </summary>
    public static async Task<bool> SetTextAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (OperatingSystem.IsWindows())
            return SetWindowsClipboardText(text);

        if (OperatingSystem.IsMacOS())
            return await RunProcessStdinAsync("pbcopy", string.Empty, text, ct).ConfigureAwait(false);

        if (OperatingSystem.IsLinux())
        {
            if (await RunProcessStdinAsync("wl-copy", string.Empty, text, ct).ConfigureAwait(false))
                return true;
            return await RunProcessStdinAsync("xclip", "-selection clipboard", text, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// 从系统剪贴板读取文本
    /// </summary>
    public static async Task<string?> GetTextAsync(CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
            return GetWindowsClipboardText();

        if (OperatingSystem.IsMacOS())
            return await RunProcessStdoutAsync("pbpaste", string.Empty, ct).ConfigureAwait(false);

        if (OperatingSystem.IsLinux())
        {
            var wl = await RunProcessStdoutAsync("wl-paste", "--no-newline", ct).ConfigureAwait(false);
            if (wl != null)
                return wl;
            return await RunProcessStdoutAsync("xclip", "-selection clipboard -o", ct).ConfigureAwait(false);
        }

        return null;
    }

    #region Windows Win32 Clipboard

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint hMem);

    private static bool SetWindowsClipboardText(string text)
    {
        if (!OpenClipboard(nint.Zero))
            return false;

        try
        {
            EmptyClipboard();
            var bytes = (nuint)((text.Length + 1) * 2);
            var hGlobal = GlobalAlloc(GmemMoveable, bytes);
            if (hGlobal == nint.Zero)
                return false;

            var target = GlobalLock(hGlobal);
            if (target == nint.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CfUnicodeText, hGlobal) == nint.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static string? GetWindowsClipboardText()
    {
        if (!OpenClipboard(nint.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == nint.Zero)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == nint.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    #endregion

    private static async Task<bool> RunProcessStdinAsync(string fileName, string arguments, string input, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false)
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            try
            {
                var writeTask = process.StandardInput.WriteAsync(input.AsMemory(), ct);
                var readOutTask = process.StandardOutput.ReadToEndAsync(ct);
                var readErrTask = process.StandardError.ReadToEndAsync(ct);

                await writeTask.ConfigureAwait(false);
                process.StandardInput.Close();
                await Task.WhenAll(readOutTask, readErrTask).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> RunProcessStdoutAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            try
            {
                var readOutTask = process.StandardOutput.ReadToEndAsync(ct);
                var readErrTask = process.StandardError.ReadToEndAsync(ct);

                await Task.WhenAll(readOutTask, readErrTask).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
                return process.ExitCode == 0 ? await readOutTask.ConfigureAwait(false) : null;
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
