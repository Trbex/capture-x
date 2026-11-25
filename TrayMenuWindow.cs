using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static RealCaptureX.CaptureMenu;

namespace RealCaptureX;

internal sealed class TrayMenuWindow : Window
{
    private readonly Grid host = new();
    private readonly Action<CaptureCommand> execute;
    private const int Width = 304;
    private bool dismissing;
    private int revision;

    public TrayMenuWindow(Action<CaptureCommand> execute)
    {
        this.execute = execute;
        Title = "REAL Capture X — Commands";
        NativeWindow.ToolWindow(this);
        Appearance.SetContent(this, Appearance.Frame(new ScrollViewer
        {
            Content = host, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }), floating: true);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
                _ = DismissAsync();
        };
        host.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            _ = DismissAsync();
        };
    }

    public void Show(NativeWindow.Rect anchor, string[] shortcuts)
    {
        revision++;
        dismissing = false;
        host.IsHitTestVisible = true;
        host.Children.Clear();

        MenuAction Action(CaptureCommand command, string label, Glyph icon, int key = -1) =>
            Mode(command, label, icon, execute, key < 0 ? null : shortcuts[key]);

        var content = new CaptureMenu("REAL Capture X", [
            new("Capture",
                Action(CaptureCommand.Region, "Capture region", Glyph.Region, 0),
                Action(CaptureCommand.Window, "Capture window", Glyph.Window, 1),
                Action(CaptureCommand.Display, "Capture display", Glyph.Display, 2),
                Action(CaptureCommand.RepeatRegion, "Repeat last region", Glyph.Repeat),
                Action(CaptureCommand.Scroll, "Scrolling capture", Glyph.Scroll),
                Action(CaptureCommand.ScrollHorizontal, "Horizontal scrolling", Glyph.HorizontalScroll),
                Action(CaptureCommand.Text, "Capture text", Glyph.Scan)),
            new("Record",
                Action(CaptureCommand.RecordRegion, "Record region", Glyph.Video),
                Action(CaptureCommand.RecordDisplay, "Record display", Glyph.Display)),
            new("Workspace",
                Action(CaptureCommand.Toolbar, "Capture toolbar", Glyph.Mark, 3),
                Action(CaptureCommand.Recent, "Quick Access", Glyph.Image, 4),
                Action(CaptureCommand.History, "Capture history", Glyph.History),
                Action(CaptureCommand.OpenStudio, "Open video in Studio", Glyph.Studio),
                Action(CaptureCommand.Settings, "Settings", Glyph.Settings)),
            new(null, Action(CaptureCommand.Exit, "Quit REAL Capture X", Glyph.Power))
        ], run =>
        {
            HideNow();
            run();
        });
        host.Children.Add(content);
        content.Measure(new Windows.Foundation.Size(Width - 2, double.PositiveInfinity));
        NativeWindow.PlacePopup(this, anchor, Width, (int)Math.Ceiling(content.DesiredSize.Height) + 2);
        ProductMotion.Enter((FrameworkElement)Content);
        Activate();
        NativeWindow.SetForegroundWindow(NativeWindow.Handle(this));
        content.FocusFirst();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (AppWindow.IsVisible && !dismissing && host.Children.Contains(content)) content.FocusFirst();
        });
    }

    public void HideNow()
    {
        revision++;
        dismissing = false;
        AppWindow.Hide();
    }

    private async Task DismissAsync()
    {
        if (dismissing || !AppWindow.IsVisible) return;
        dismissing = true;
        var current = ++revision;
        host.IsHitTestVisible = false;
        await ProductMotion.ExitAsync((FrameworkElement)Content);
        if (current == revision) HideNow();
    }
}
