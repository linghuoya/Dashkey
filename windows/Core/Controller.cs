using System.Windows.Threading;
using DashKey.Native;
using DashKey.Views;

namespace DashKey.Core;

/// <summary>
/// 主逻辑：扫描应用、注册热键、长按 Alt 轮询、面板显隐、托盘菜单。
/// </summary>
internal sealed class Controller : IDisposable
{
    private const int AppHotkeyIdBase = 1;
    private const int PanelHotkeyId = 1000;

    /// <summary>VK_OEM_3 —— 反引号键，位置对应 macOS 版的 Space 邻键。</summary>
    private const uint PanelVirtualKey = 0xC0;

    /// <summary>槽位顺序与 macOS 版一致：数字行 → QWERTY 三行，共 36 个。</summary>
    private static readonly string[] Slots =
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
        "A", "S", "D", "F", "G", "H", "J", "K", "L",
        "Z", "X", "C", "V", "B", "N", "M",
    };

    private readonly HotkeyManager _hotkeys = new();

    private List<AppEntry> _apps = new();
    private HudWindow? _hud;
    private SettingsWindow? _settingsWindow;
    private TrayIconManager? _tray;

    private DispatcherTimer? _altTimer;
    private DispatcherTimer? _holdTimer;
    private bool _altDown;

    /// <summary>面板是通过热键「开关」出来的：此时松开 Alt 不应该把它收起来。</summary>
    private bool _pinnedOpen;

    private bool _disposed;

    public void Start()
    {
        _tray = new TrayIconManager(
            onToggle: ToggleHud,
            onSettings: ShowSettings,
            onReload: ReloadApps,
            onExit: Shutdown);

        ReloadApps();
        ApplyTriggerSettings();

        Log.Info($"启动完成：{_apps.Count} 个任务栏固定项");
    }

    // ---------------- 应用列表 ----------------

    public void ReloadApps()
    {
        _apps = AppScanner.Scan();
        RegisterAppHotkeys();

        if (_hud is { IsVisible: true }) _hud.ShowApps(_apps, Slots);

        var preview = string.Join(", ", _apps.Take(5).Select(a => a.Name));
        Log.Info($"读取到 {_apps.Count} 个应用 → 前 5: {preview}");
    }

    // ---------------- 热键 ----------------

    private static uint PrefixModifier => Settings.Prefix == HotkeyPrefix.CtrlAlt
        ? Win32.MOD_CONTROL | Win32.MOD_ALT
        : Win32.MOD_ALT;

    private void RegisterAppHotkeys()
    {
        for (var id = AppHotkeyIdBase; id < AppHotkeyIdBase + Slots.Length; id++) _hotkeys.Unregister(id);

        var modifiers = PrefixModifier;
        var count = Math.Min(_apps.Count, Slots.Length);
        var failed = 0;

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var key = (uint)Slots[i][0];

            if (!_hotkeys.Register(AppHotkeyIdBase + i, modifiers, key, () => ActivateSlot(index)))
            {
                failed++;
                Log.Info($"热键注册失败：{Slots[i]}");
            }
        }

        if (failed > 0) Log.Info($"{failed} 个热键被其他程序占用，对应应用只能点击面板图标切换");
    }

    private void RegisterPanelHotkey()
    {
        _hotkeys.Unregister(PanelHotkeyId);

        if (!_hotkeys.Register(PanelHotkeyId, PrefixModifier, PanelVirtualKey, ToggleHud))
        {
            Log.Info("面板热键注册失败（可能被其他程序占用）");
        }
    }

    public void ApplyTriggerSettings()
    {
        if (Settings.HoldAltEnabled) StartAltPolling();
        else StopAltPolling();

        ReapplyHotkeys();
    }

    /// <summary>前缀或开关变化后重新注册全部热键。</summary>
    public void ReapplyHotkeys()
    {
        RegisterAppHotkeys();

        if (Settings.PanelHotkeyEnabled) RegisterPanelHotkey();
        else _hotkeys.Unregister(PanelHotkeyId);
    }

    public int AppCount => _apps.Count;

    // ---------------- 长按 Alt ----------------

    private void StartAltPolling()
    {
        if (_altTimer is not null) return;

        _altTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        _altTimer.Tick += OnAltTick;
        _altTimer.Start();

        Log.Info("已开启长按 Alt 轮询");
    }

    private void StopAltPolling()
    {
        if (_altTimer is null) return;

        _altTimer.Stop();
        _altTimer.Tick -= OnAltTick;
        _altTimer = null;

        _altDown = false;
        CancelHoldTimer();
    }

    private void OnAltTick(object? sender, EventArgs e)
    {
        var down = (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;
        if (down == _altDown) return;

        _altDown = down;

        if (down)
        {
            StartHoldTimer();
            return;
        }

        CancelHoldTimer();

        if (_pinnedOpen)
        {
            _pinnedOpen = false;
            return;
        }

        HideHud();
    }

    private void StartHoldTimer()
    {
        if (_holdTimer is not null) return;

        _holdTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Settings.HoldDuration),
        };
        _holdTimer.Tick += OnHoldElapsed;
        _holdTimer.Start();
    }

    private void OnHoldElapsed(object? sender, EventArgs e)
    {
        CancelHoldTimer();
        ShowHud(pinned: false);
    }

    private void CancelHoldTimer()
    {
        if (_holdTimer is null) return;

        _holdTimer.Stop();
        _holdTimer.Tick -= OnHoldElapsed;
        _holdTimer = null;
    }

    // ---------------- 面板 ----------------

    private void ToggleHud()
    {
        if (_hud is { IsVisible: true })
        {
            HideHud();
            return;
        }

        ShowHud(pinned: true);
    }

    private void ShowHud(bool pinned)
    {
        if (_apps.Count == 0) _apps = AppScanner.Scan();

        _hud ??= CreateHud();
        _hud.ShowApps(_apps, Slots);
        _pinnedOpen = pinned;
    }

    private void HideHud()
    {
        _hud?.Hide();
        _pinnedOpen = false;
    }

    private HudWindow CreateHud()
    {
        var hud = new HudWindow();
        hud.ItemActivated += ActivateSlot;
        return hud;
    }

    private void ActivateSlot(int index)
    {
        CancelHoldTimer();
        HideHud();

        if (index < 0 || index >= _apps.Count) return;

        AppLauncher.Activate(_apps[index]);
    }

    // ---------------- 设置 / 退出 ----------------

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.WindowState = System.Windows.WindowState.Normal;
        _settingsWindow.Activate();
    }

    private static void Shutdown()
    {
        System.Windows.Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAltPolling();
        CancelHoldTimer();

        _hotkeys.Dispose();
        _tray?.Dispose();

        _settingsWindow?.Close();
        _hud?.Close();
    }
}
