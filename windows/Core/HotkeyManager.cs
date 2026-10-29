using System.Windows.Interop;
using DashKey.Native;

namespace DashKey.Core;

/// <summary>
/// 全局热键。用 RegisterHotKey 挂在一个不可见的 HwndSource 上，
/// 不注入任何进程、不装键盘钩子，因此不需要管理员权限。
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly List<int> _registered = new();
    private bool _disposed;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("DashKey.HotkeySink")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = Win32.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>注册一个热键，返回是否成功（组合已被其他程序占用时会失败）。</summary>
    public bool Register(int id, uint modifiers, uint virtualKey, Action action)
    {
        Unregister(id);
        _actions[id] = action;

        if (Win32.RegisterHotKey(_source.Handle, id, modifiers | Win32.MOD_NOREPEAT, virtualKey))
        {
            _registered.Add(id);
            return true;
        }

        _actions.Remove(id);
        return false;
    }

    public void Unregister(int id)
    {
        if (!_registered.Remove(id)) return;

        Win32.UnregisterHotKey(_source.Handle, id);
        _actions.Remove(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToArray()) Unregister(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
