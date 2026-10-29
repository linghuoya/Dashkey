using DashKey.Native;

namespace DashKey.Core;

/// <summary>快捷键前缀。</summary>
internal enum HotkeyPrefix
{
    Alt = 0,
    CtrlAlt = 1,
}

/// <summary>图标清晰度：数值越大越清晰，也越占内存。</summary>
internal enum IconQuality
{
    Low = 0,
    Normal = 1,
    High = 2,
}

/// <summary>
/// 偏好设置。与 macOS 版的 UserDefaults 对应，Windows 上存在
/// HKCU\Software\DashKey；开机自启走 Run 键。
/// </summary>
internal static class Settings
{
    private const string AppKey = @"Software\DashKey";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DashKey";

    public const double DefaultHoldDuration = 2.0;

    /// <summary>长按呼出面板的时长（秒），允许 0.5 ~ 3.0。</summary>
    public static double HoldDuration
    {
        get
        {
            var v = Reg.ReadDouble(AppKey, nameof(HoldDuration));
            return v.HasValue && v.Value > 0 ? v.Value : DefaultHoldDuration;
        }
        set => Reg.WriteDouble(AppKey, nameof(HoldDuration), value);
    }

    /// <summary>是否启用「长按 Alt 呼出面板」。</summary>
    public static bool HoldAltEnabled
    {
        get => ReadFlag(nameof(HoldAltEnabled), true);
        set => WriteFlag(nameof(HoldAltEnabled), value);
    }

    /// <summary>是否启用面板开关热键。</summary>
    public static bool PanelHotkeyEnabled
    {
        get => ReadFlag(nameof(PanelHotkeyEnabled), true);
        set => WriteFlag(nameof(PanelHotkeyEnabled), value);
    }

    public static HotkeyPrefix Prefix
    {
        get
        {
            var v = Reg.ReadInt(AppKey, nameof(Prefix));
            return v == (int)HotkeyPrefix.CtrlAlt ? HotkeyPrefix.CtrlAlt : HotkeyPrefix.Alt;
        }
        set => Reg.WriteInt(AppKey, nameof(Prefix), (int)value);
    }

    public static IconQuality IconLevel
    {
        get
        {
            var v = Reg.ReadInt(AppKey, nameof(IconLevel));
            return v is >= 0 and <= 2 ? (IconQuality)v.Value : IconQuality.Normal;
        }
        set => Reg.WriteInt(AppKey, nameof(IconLevel), (int)value);
    }

    /// <summary>按清晰度换算成实际解码的图标像素尺寸。</summary>
    public static int IconPixelSize => IconLevel switch
    {
        IconQuality.Low => 48,
        IconQuality.High => 96,
        _ => 64,
    };

    /// <summary>开机自启：直接看 Run 键里有没有我们的项，与系统状态保持一致。</summary>
    public static bool LaunchAtLogin
    {
        get => Reg.ReadString(RunKey, RunValueName) is not null;
        set
        {
            if (value)
            {
                Reg.WriteString(RunKey, RunValueName, "\"" + ExecutablePath + "\"");
            }
            else
            {
                Reg.DeleteValue(RunKey, RunValueName);
            }
        }
    }

    public static string ExecutablePath
        => Environment.ProcessPath
           ?? Path.ChangeExtension(typeof(Settings).Assembly.Location, ".exe");

    private static bool ReadFlag(string name, bool defaultValue)
    {
        var v = Reg.ReadInt(AppKey, name);
        return v.HasValue ? v.Value != 0 : defaultValue;
    }

    private static void WriteFlag(string name, bool value) => Reg.WriteInt(AppKey, name, value ? 1 : 0);
}
