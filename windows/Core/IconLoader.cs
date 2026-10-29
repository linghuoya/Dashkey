using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DashKey.Native;

namespace DashKey.Core;

/// <summary>
/// 取 Shell 图标并降采样。
/// 优先用系统大图列表（JUMBO 256），失败再退 32×32，保证任何机器都有图标。
/// </summary>
internal static class IconLoader
{
    public static ImageSource? Load(string path, int pixelSize)
    {
        var hIcon = GetShellIcon(path);
        if (hIcon == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return Downsample(source, pixelSize);
        }
        catch
        {
            return null;
        }
        finally
        {
            Win32.DestroyIcon(hIcon);
        }
    }

    /// <summary>把图标缩到目标像素尺寸，避免几十张大图常驻内存。</summary>
    private static ImageSource Downsample(BitmapSource source, int pixelSize)
    {
        if (pixelSize <= 0 || source.PixelWidth <= pixelSize) return source;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, pixelSize, pixelSize));
        }

        var target = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static IntPtr GetShellIcon(string path)
    {
        var info = new Win32.SHFILEINFO();
        var size = (uint)Marshal.SizeOf<Win32.SHFILEINFO>();

        if (Win32.SHGetFileInfo(path, 0, ref info, size, Win32.SHGFI_SYSICONINDEX) != IntPtr.Zero)
        {
            foreach (var listType in new[] { Win32.SHIL_JUMBO, Win32.SHIL_EXTRALARGE, Win32.SHIL_LARGE })
            {
                var iid = Win32.IID_IImageList;
                if (Win32.SHGetImageList(listType, ref iid, out var imageList) != 0 || imageList is null) continue;

                try
                {
                    if (imageList.GetIcon(info.iIcon, Win32.ILD_TRANSPARENT, out var hIcon) == 0 && hIcon != IntPtr.Zero)
                    {
                        return hIcon;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(imageList);
                }
            }
        }

        // 兜底：32×32
        var fallback = new Win32.SHFILEINFO();
        var ok = Win32.SHGetFileInfo(path, 0, ref fallback, size, Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON);
        return ok == IntPtr.Zero ? IntPtr.Zero : fallback.hIcon;
    }
}
