using DashKey.Native;

namespace DashKey.Core;

internal static class Dpi
{
    /// <summary>取指定屏幕坐标所在显示器的缩放系数（1.0 = 100%）。</summary>
    public static double ScaleAt(int x, int y)
    {
        try
        {
            var point = new Win32.POINT { X = x, Y = y };
            var monitor = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero
                && Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
                && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return 1.0;
    }
}
