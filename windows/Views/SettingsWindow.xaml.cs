using System.Windows;
using DashKey.Core;

namespace DashKey.Views;

public partial class SettingsWindow : Window
{
    private readonly Controller _controller;

    internal SettingsWindow(Controller controller)
    {
        _controller = controller;
        InitializeComponent();

        // 先填值再挂事件，避免初始化时把默认值写回注册表
        HoldSlider.Value = Math.Clamp(Settings.HoldDuration, HoldSlider.Minimum, HoldSlider.Maximum);
        HoldAltCheck.IsChecked = Settings.HoldAltEnabled;
        PanelHotkeyCheck.IsChecked = Settings.PanelHotkeyEnabled;
        PrefixCombo.SelectedIndex = (int)Settings.Prefix;
        QualityCombo.SelectedIndex = (int)Settings.IconLevel;
        LaunchCheck.IsChecked = Settings.LaunchAtLogin;

        UpdateHoldLabel();
        UpdatePrefixHint();
        UpdateStatus();

        HoldSlider.ValueChanged += OnHoldChanged;
        HoldAltCheck.Click += OnTriggerChanged;
        PanelHotkeyCheck.Click += OnTriggerChanged;
        PrefixCombo.SelectionChanged += OnPrefixChanged;
        QualityCombo.SelectionChanged += OnQualityChanged;
        LaunchCheck.Click += OnLaunchChanged;
        ReloadButton.Click += OnReloadClicked;
        CloseButton.Click += (_, _) => Close();
    }

    private void OnHoldChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Settings.HoldDuration = HoldSlider.Value;
        UpdateHoldLabel();
    }

    private void OnTriggerChanged(object sender, RoutedEventArgs e)
    {
        Settings.HoldAltEnabled = HoldAltCheck.IsChecked == true;
        Settings.PanelHotkeyEnabled = PanelHotkeyCheck.IsChecked == true;
        _controller.ApplyTriggerSettings();
        UpdateStatus();
    }

    private void OnPrefixChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Settings.Prefix = PrefixCombo.SelectedIndex == (int)HotkeyPrefix.CtrlAlt
            ? HotkeyPrefix.CtrlAlt
            : HotkeyPrefix.Alt;

        _controller.ReapplyHotkeys();
        UpdatePrefixHint();
        UpdateStatus();
    }

    private void OnQualityChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Settings.IconLevel = (IconQuality)Math.Clamp(QualityCombo.SelectedIndex, 0, 2);
        _controller.ReloadApps();
        UpdateStatus();
    }

    private void OnLaunchChanged(object sender, RoutedEventArgs e)
    {
        Settings.LaunchAtLogin = LaunchCheck.IsChecked == true;
    }

    private void OnReloadClicked(object sender, RoutedEventArgs e)
    {
        _controller.ReloadApps();
        UpdateStatus();
    }

    private void UpdateHoldLabel() => HoldValueText.Text = $"{HoldSlider.Value:0.0} s";

    private void UpdatePrefixHint()
    {
        PrefixHint.Text = Settings.Prefix == HotkeyPrefix.CtrlAlt
            ? "Ctrl + Alt + 键，面板 Ctrl + Alt + `"
            : "Alt + 键，面板 Alt + `";
    }

    private void UpdateStatus()
    {
        var count = _controller.AppCount;
        StatusText.Text = count == 0
            ? "没有读到任务栏固定项。先把应用固定到任务栏，再点「重新读取」。"
            : $"已读取 {count} 个任务栏固定项，对应前 {count} 个快捷键槽位。";
    }
}
