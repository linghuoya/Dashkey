using System.Runtime.InteropServices;
using System.Text;

namespace DashKey.Native;

/// <summary>
/// 解析 .lnk 快捷方式指向的目标可执行文件，用于判断进程是否已在运行。
/// </summary>
internal static class ShellLink
{
    private const uint STGM_READ = 0;
    private const uint SLR_NO_UI = 0x0001;
    private const uint SLR_NOSEARCH = 0x0010;

    public static string? ResolveTarget(string lnkPath)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLinkCoClass();

            if (link is IPersistFile persistFile)
            {
                persistFile.Load(lnkPath, STGM_READ);
            }

            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            var path = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;

            // 路径为空或已失效时才尝试解析（Resolve 可能较慢，不做常规调用）
            link.Resolve(IntPtr.Zero, SLR_NO_UI | SLR_NOSEARCH);
            buffer.Clear();
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            path = buffer.ToString();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link is not null)
            {
                try { Marshal.FinalReleaseComObject(link); } catch { /* ignore */ }
            }
        }
    }
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass
{
}

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

    void GetIDList(out IntPtr ppidl);

    void SetIDList(IntPtr pidl);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out short pwHotkey);

    void SetHotkey(short wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

    void Resolve(IntPtr hwnd, uint fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

    void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);

    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}
