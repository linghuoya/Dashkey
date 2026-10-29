using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DashKey.Core;
using DashKey.Native;
using Drawing = System.Drawing;

namespace DashKey.Views;

/// <summary>
/// 快捷键面板：无边框、半透明、置顶、不抢焦点，鼠标所在屏幕居中。
/// </summary>
public partial class HudWindow : Window
{
    private const double ItemSize = 52;
    private const double IconSize = 36;
    private const double Spacing = 8;
    private const double PanelPadding = 10;
    private const double ShadowMargin = 12;
    private const double MaxScreenUsage = 0.9;
    private const double MinScale = 0.45;

    private static readonly Brush BadgeBackground = Frozen(Color.FromRgb(0x57, 0x57, 0x57));
    private static readonly Brush BadgeBorder = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HoverBackground = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    public event Action<int>? ItemActivated;

    public HudWindow()
    {
        InitializeComponent();

        Panel.Margin = new Thickness(ShadowMargin);
        Panel.Padding = new Thickness(PanelPadding);

        SourceInitialized += OnSourceInitialized;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 不激活、不进 Alt+Tab，但鼠标点击仍然能收到
        var style = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW;
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, style);
    }

    public void ShowApps(IReadOnlyList<AppEntry> apps, IReadOnlyList<string> labels)
    {
        if (apps.Count == 0)
        {
            Hide();
            return;
        }

        var cursor = Drawing.Cursor.Position;
        var screen = Drawing.Screen.FromPoint(cursor);
        var dpiScale = Dpi.ScaleAt(cursor.X, cursor.Y);
        var maxWidthDip = screen.WorkingArea.Width / dpiScale * MaxScreenUsage;

        // 单行布局：放不下就逐步缩小图标，尽量把 36 个全塞进一行
        var scale = 1.0;
        var columns = FittedColumns(scale, maxWidthDip);

        while (columns < apps.Count && scale > MinScale)
        {
            var next = Math.Max(MinScale, scale * 0.9);
            if (Math.Abs(next - scale) < 0.001) break;

            scale = next;
            columns = FittedColumns(scale, maxWidthDip);
        }

        columns = Math.Min(columns, apps.Count);

        BuildItems(apps, labels, columns, scale);

        var unit = ItemSize * scale;
        Width = columns * unit + Math.Max(0, columns - 1) * Spacing + PanelPadding * 2 + ShadowMargin * 2;
        Height = unit + PanelPadding * 2 + ShadowMargin * 2;

        UpdateLayout();

        if (!IsVisible)
        {
            // 先以全透明显示，摆好位置再露出来，避免在默认位置闪一下
            Opacity = 0;
            Show();
            UpdateLayout();
            CenterOn(screen.WorkingArea);
            Opacity = 1;
        }
        else
        {
            CenterOn(screen.WorkingArea);
        }
    }

    private static int FittedColumns(double scale, double maxWidthDip)
    {
        var unit = ItemSize * scale;
        var fitted = (int)Math.Floor((maxWidthDip - PanelPadding * 2 + Spacing) / (unit + Spacing));
        return Math.Max(1, fitted);
    }

    private void CenterOn(Drawing.Rectangle workArea)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Win32.GetWindowRect(hwnd, out var rect)) return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = workArea.Left + (workArea.Width - width) / 2;
        var y = workArea.Top + (workArea.Height - height) / 2;

        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    private void BuildItems(IReadOnlyList<AppEntry> apps, IReadOnlyList<string> labels, int columns, double scale)
    {
        ItemsRow.Children.Clear();

        var unit = ItemSize * scale;

        for (var i = 0; i < columns; i++)
        {
            var entry = apps[i];
            var cell = new Grid
            {
                Width = unit,
                Height = unit,
                Margin = new Thickness(i == 0 ? 0 : Spacing, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = entry.Name,
                Background = Brushes.Transparent,
            };

            var hover = new Border
            {
                CornerRadius = new CornerRadius(8 * scale),
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
            };
            cell.Children.Add(hover);

            var icon = entry.Icon;
            UIElement content = icon is not null
                ? CreateIconView(icon, scale)
                : CreateFallbackView(entry.Name, scale);
            cell.Children.Add(content);

            if (i < labels.Count) cell.Children.Add(CreateBadge(labels[i], scale));

            var index = i;
            cell.MouseEnter += (_, _) => hover.Background = HoverBackground;
            cell.MouseLeave += (_, _) => hover.Background = Brushes.Transparent;
            cell.MouseLeftButtonUp += (_, _) => ItemActivated?.Invoke(index);

            ItemsRow.Children.Add(cell);
        }
    }

    private static Image CreateIconView(ImageSource source, double scale)
    {
        var image = new Image
        {
            Source = source,
            Width = IconSize * scale,
            Height = IconSize * scale,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2 * scale),
            IsHitTestVisible = false,
        };

        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static TextBlock CreateFallbackView(string name, double scale)
    {
        var initial = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();

        return new TextBlock
        {
            Text = initial,
            FontSize = 18 * scale,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    /// <summary>快捷键角标，压在图标右下角。</summary>
    private static Border CreateBadge(string text, double scale)
    {
        var badge = new Border
        {
            Width = 15 * scale,
            Height = 13 * scale,
            CornerRadius = new CornerRadius(3.5 * scale),
            Background = BadgeBackground,
            BorderBrush = BadgeBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 5 * scale, 5 * scale),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 10 * scale,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };

        return badge;
    }
}
