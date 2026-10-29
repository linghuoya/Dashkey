using System.Text;
using System.Windows.Media;
using DashKey.Native;

namespace DashKey.Core;

/// <summary>一个可切换的应用。</summary>
public sealed record AppEntry(string Name, string ShortcutPath, string? TargetPath, ImageSource? Icon);

/// <summary>
/// 扫描「任务栏固定项」——Windows 上对应 macOS 版读取 Dock 的那一步。
/// 固定项就是 %AppData%\...\User Pinned\TaskBar 下的 .lnk；
/// 真实排列顺序存放在 Taskband 的二进制值里，解析失败则退化为按名称排序。
/// </summary>
internal static class AppScanner
{
    /// <summary>与快捷键槽位一一对应。</summary>
    public const int MaxSlots = 36;

    private const string TaskbandKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    public static List<AppEntry> Scan()
    {
        var pixelSize = Settings.IconPixelSize;
        var shortcuts = CollectShortcuts();
        var result = new List<AppEntry>(Math.Min(shortcuts.Count, MaxSlots));

        foreach (var lnk in shortcuts.Take(MaxSlots))
        {
            var icon = IconLoader.Load(lnk, pixelSize);
            var target = ShellLink.ResolveTarget(lnk);
            result.Add(new AppEntry(Path.GetFileNameWithoutExtension(lnk), lnk, target, icon));
        }

        return result;
    }

    private static string PinnedTaskbarDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    private static List<string> CollectShortcuts()
    {
        var pinned = EnumerateLinks(PinnedTaskbarDir);
        if (pinned.Count == 0) return EnumerateStartMenuLinks();

        var ordered = new List<string>();

        foreach (var full in ReadTaskbandOrder())
        {
            var name = Path.GetFileName(full);
            var match = pinned.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));

            if (match is not null && !ordered.Contains(match, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(match);
            }
        }

        foreach (var path in pinned.OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase))
        {
            if (!ordered.Contains(path, StringComparer.OrdinalIgnoreCase)) ordered.Add(path);
        }

        return ordered;
    }

    /// <summary>从 Taskband\Favorites 里按出现顺序抠出 .lnk 全路径。</summary>
    private static IEnumerable<string> ReadTaskbandOrder()
    {
        var blob = Reg.ReadBinary(TaskbandKey, "Favorites");
        if (blob is null) yield break;

        var buffer = new StringBuilder();

        for (var i = 0; i + 1 < blob.Length; i += 2)
        {
            var ch = (char)(blob[i] | (blob[i + 1] << 8));

            if (ch >= 0x20 && ch < 0x7F)
            {
                buffer.Append(ch);
                continue;
            }

            if (buffer.Length == 0) continue;

            var text = buffer.ToString();
            buffer.Clear();

            if (text.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) yield return text;
        }
    }

    /// <summary>没有固定项时兜底：开始菜单里的快捷方式。</summary>
    private static List<string> EnumerateStartMenuLinks()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };

        var all = new List<string>();
        foreach (var root in roots)
        {
            all.AddRange(EnumerateLinks(root, recursive: true));
        }

        return all
            .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<string> EnumerateLinks(string directory, bool recursive = false)
    {
        try
        {
            if (!Directory.Exists(directory)) return new List<string>();

            return Directory
                .EnumerateFiles(directory, "*.lnk", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error($"枚举快捷方式失败：{directory}", ex);
            return new List<string>();
        }
    }
}
