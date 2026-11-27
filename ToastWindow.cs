using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace RealCaptureX;

/// <summary>Small confirmation ("Screenshot copied") that rises from the bottom of the active display.</summary>
internal sealed class ToastWindow : Window
{
    private const int PanelWidth = 336;
    private const int PanelHeight = 64;
    private static ToastWindow? shared;
    private readonly GlassSurface glass;
    private readonly TextBlock title = Appearance.Label("", 13, strong: true);
    private readonly TextBlock detail = Appearance.Label("", 12, Appearance.Muted);
    private readonly Border badge;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private int revision;

    private ToastWindow()
    {
        Title = "REAL Capture X — Notification";
        NativeWindow.ToolWindow(this);
        var layout = new Grid { ColumnSpacing = 12, Padding = new Thickness(14, 0, 8, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        badge = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Appearance.Tint,
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
        };
        layout.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        title.TextTrimming = detail.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        layout.Children.Add(text);
        var close = Appearance.IconButton(Glyph.Close, "Dismiss notification", () => _ = HideAsync(revision), 28);
        close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(close, 2);
        layout.Children.Add(close);
        AutomationProperties.SetLiveSetting(title, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        glass = Appearance.Float(this, layout, radius: 14);
        glass.Panel.PointerEntered += (_, _) => timer!.Stop();
        glass.Panel.PointerExited += (_, _) => timer!.Start();
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2.4);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => _ = HideAsync(revision);
#if DEBUG
        if (!QaMode.Inspect)
#endif
            NativeWindow.ExcludeFromCapture(this);
    }

    /// <summary>Hides immediately (before a capture, so the toast never lands in a screenshot).</summary>
    internal static void HideNow()
    {
        if (shared is null || !shared.AppWindow.IsVisible) return;
        shared.revision++;
        shared.timer.Stop();
        shared.AppWindow.Hide();
        shared.glass.Conceal();
    }

    /// <summary>Shows a confirmation without taking focus from the user's current app.</summary>
    internal static void Show(string heading, string? body = null, Glyph glyph = Glyph.Check)
    {
        shared ??= new ToastWindow();
        shared.Present(heading, body, glyph);
    }

    private void Present(string heading, string? body, Glyph glyph)
    {
        revision++;
        title.Text = heading;
        detail.Text = body ?? "";
        detail.Visibility = body is null ? Visibility.Collapsed : Visibility.Visible;
        badge.Child = Symbols.Draw(glyph, 16, Appearance.Text);
        var visible = AppWindow.IsVisible;
        if (!visible)
            NativeWindow.Place(this, NativeWindow.CursorMonitor(), GlassSurface.Outer(PanelWidth), GlassSurface.Outer(PanelHeight),
                .5, .965, GlassSurface.Margin);
        AppWindow.Show(false);
        NativeWindow.SetWindowPos(NativeWindow.Handle(this), NativeWindow.Topmost, 0, 0, 0, 0, NativeWindow.NoActivate | 0x0003);
        if (visible) glass.Settle();
        else glass.Enter(rise: 14, fromScale: .96f, spec: SpringSpec.Snappy, origin: new System.Numerics.Vector2(.5f, 1));
        timer.Stop();
        timer.Start();
    }

    private async Task HideAsync(int expected)
    {
        if (expected != revision || !AppWindow.IsVisible) return;
        timer.Stop();
        await glass.ExitAsync(drop: 10);
        if (expected != revision) return;
        AppWindow.Hide();
        glass.Conceal();
    }
}
