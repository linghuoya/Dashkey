using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DashKey.Core;

/// <summary>
/// 托盘图标与右键菜单。Windows 没有菜单栏，托盘就是 macOS 版状态栏图标的对应物。
/// </summary>
internal sealed class TrayIconManager : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;

    public TrayIconManager(Action onToggle, Action onSettings, Action onReload, Action onExit)
    {
        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
        };

        _menu.Items.Add("设置…", null, (_, _) => onSettings());
        _menu.Items.Add("重新读取任务栏固定项", null, (_, _) => onReload());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => onExit());

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DashKey — 快捷键切换应用",
            ContextMenuStrip = _menu,
            Visible = true,
        };

        // 左键双击直接开关面板，右键出菜单
        _icon.MouseDoubleClick += (_, _) => onToggle();
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/DashKey.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;

            if (stream is not null)
            {
                using (stream)
                {
                    return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("加载托盘图标失败", ex);
        }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
