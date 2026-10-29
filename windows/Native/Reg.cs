using System.Runtime.InteropServices;
using System.Text;

namespace DashKey.Native;

/// <summary>
/// 最小注册表封装（advapi32 直调）。配置与开机自启都走 HKCU，不需要管理员权限。
/// </summary>
internal static class Reg
{
    /// <summary>HKEY_CURRENT_USER 预定义句柄（32 位值，按 LONG 符号扩展到指针宽度）。</summary>
    private static readonly IntPtr HKCU = new(unchecked((int)0x80000001));

    private const int KEY_READ = 0x20019;
    private const int KEY_READ_WRITE = 0x20006;

    private const int REG_SZ = 1;
    private const int REG_BINARY = 3;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
    private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegCreateKeyExW")]
    private static extern int RegCreateKeyExW(IntPtr hKey, string lpSubKey, uint reserved, string? lpClass,
        uint dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out int lpdwDisposition);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueExW(IntPtr hKey, string lpValueName, IntPtr lpReserved,
        out uint lpType, [In, Out] byte[]? lpData, ref uint lpcbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueExW(IntPtr hKey, string lpValueName, uint reserved,
        uint dwType, byte[] lpData, uint cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegDeleteValueW")]
    private static extern int RegDeleteValueW(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);

    // ---------------- 读 ----------------

    public static string? ReadString(string subKey, string name)
    {
        var bytes = ReadValue(subKey, name, out var type);
        if (bytes is null || type != REG_SZ) return null;
        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    public static byte[]? ReadBinary(string subKey, string name)
        => ReadValue(subKey, name, out var type) is { } bytes && type == REG_BINARY ? bytes : null;

    public static int? ReadInt(string subKey, string name)
        => int.TryParse(ReadString(subKey, name), out var v) ? v : null;

    public static double? ReadDouble(string subKey, string name)
        => double.TryParse(ReadString(subKey, name), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static byte[]? ReadValue(string subKey, string name, out int type)
    {
        type = 0;
        if (RegOpenKeyExW(HKCU, subKey, 0, KEY_READ, out var hKey) != 0) return null;

        try
        {
            uint size = 0;
            if (RegQueryValueExW(hKey, name, IntPtr.Zero, out var t, null, ref size) != 0 || size == 0) return null;

            var buffer = new byte[size];
            if (RegQueryValueExW(hKey, name, IntPtr.Zero, out t, buffer, ref size) != 0) return null;

            type = (int)t;
            if (buffer.Length != (int)size) Array.Resize(ref buffer, (int)size);
            return buffer;
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    // ---------------- 写 ----------------

    public static void WriteString(string subKey, string name, string value)
        => WriteValue(subKey, name, Encoding.Unicode.GetBytes(value + "\0"), REG_SZ);

    public static void WriteInt(string subKey, string name, int value)
        => WriteString(subKey, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static void WriteDouble(string subKey, string name, double value)
        => WriteString(subKey, name, value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

    private static void WriteValue(string subKey, string name, byte[] data, int type)
    {
        if (RegCreateKeyExW(HKCU, subKey, 0, null, 0, KEY_READ_WRITE, IntPtr.Zero, out var hKey, out _) != 0) return;

        try
        {
            RegSetValueExW(hKey, name, 0, (uint)type, data, (uint)data.Length);
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }

    public static void DeleteValue(string subKey, string name)
    {
        if (RegOpenKeyExW(HKCU, subKey, 0, KEY_READ_WRITE, out var hKey) != 0) return;

        try
        {
            RegDeleteValueW(hKey, name);
        }
        finally
        {
            RegCloseKey(hKey);
        }
    }
}
