using System.Diagnostics;
using DashKey.Native;

namespace DashKey.Core;

/// <summary>
/// 切换到目标应用：已运行则把窗口抬到前台（已在前台则最小化），未运行则通过 Shell 启动。
/// </summary>
internal static class AppLauncher
{
    public static void Activate(AppEntry app)
    {
        var hwnd = FindVisibleWindow(app.TargetPath);

        if (hwnd != IntPtr.Zero)
        {
            if (Win32.GetForegroundWindow() == hwnd)
            {
                Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);
                return;
            }

            ForceForeground(hwnd);
            return;
        }

        Launch(app.ShortcutPath);
    }

    /// <summary>按目标 exe 的进程名找已运行实例，取第一个有主窗口的进程。</summary>
    private static IntPtr FindVisibleWindow(string? targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return IntPtr.Zero;

        var processName = Path.GetFileNameWithoutExtension(targetPath);
        if (string.IsNullOrEmpty(processName)) return IntPtr.Zero;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception ex)
        {
            Log.Error($"枚举进程失败：{processName}", ex);
            return IntPtr.Zero;
        }

        var handle = IntPtr.Zero;

        foreach (var process in processes)
        {
            try
            {
                if (handle == IntPtr.Zero && process.MainWindowHandle != IntPtr.Zero)
                {
                    handle = process.MainWindowHandle;
                }
            }
            catch
            {
                // 受保护进程读不到主窗口，跳过
            }
            finally
            {
                process.Dispose();
            }
        }

        return handle;
    }

    /// <summary>
    /// 抢前台。Windows 有前台锁，直接 SetForegroundWindow 经常无效，
    /// 这里先 AttachThreadInput 借前台线程的输入状态，失败再走 SwitchToThisWindow 兜底。
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);

        var foreground = Win32.GetForegroundWindow();
        var foregroundThread = Win32.GetWindowThreadProcessId(foreground, out _);
        var currentThread = Win32.GetCurrentThreadId();
        var attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = Win32.AttachThreadInput(currentThread, foregroundThread, true);
            }

            Win32.BringWindowToTop(hwnd);
            Win32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Win32.AttachThreadInput(currentThread, foregroundThread, false);
        }

        if (Win32.GetForegroundWindow() != hwnd)
        {
            try
            {
                Win32.SwitchToThisWindow(hwnd, true);
            }
            catch (EntryPointNotFoundException)
            {
                Log.Info("SwitchToThisWindow 不可用，已忽略");
            }
        }
    }

    private static void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"启动失败：{path}", ex);
        }
    }
}
