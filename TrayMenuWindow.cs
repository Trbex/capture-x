using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static RealCaptureX.CaptureMenu;

namespace RealCaptureX;

/// <summary>
/// The one popup menu window of the app: tray menu, toolbar quick menu and every context menu. It is a
/// glass window positioned next to an anchor rectangle and dismissed on deactivation or Escape.
/// </summary>
internal sealed class TrayMenuWindow : Window
{
    internal static TrayMenuWindow? Shared { get; private set; }

    private readonly Grid host = new();
    private readonly ScrollViewer scroller;
    private readonly GlassSurface glass;
    private readonly Action<CaptureCommand> execute;
    private Action? closed;
    private bool dismissing;
    private int revision;
    private float exitDrop;

    public TrayMenuWindow(Action<CaptureCommand> execute)
    {
        this.execute = execute;
        Shared = this;
        Title = "REAL Capture X — Commands";
        NativeWindow.ToolWindow(this);
        NativeWindow.ExcludeOverlayFromCapture(this);
        scroller = new ScrollViewer
        {
            Content = host, HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        glass = Appearance.Float(this, scroller, radius: 14);

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

    public void Show(NativeWindow.Rect anchor, string[] shortcuts, bool compact)
    {
        MenuAction Action(CaptureCommand command, string label, Glyph icon, int key = -1) =>
            Mode(command, label, icon, execute, key < 0 ? null : shortcuts[key]);

        var capture = new MenuSection("Capture",
            Action(CaptureCommand.Region, "Capture region", Glyph.Region, 0),
            Action(CaptureCommand.Window, "Capture window", Glyph.Window, 1),
            Action(CaptureCommand.Display, "Capture display", Glyph.Display, 2),
            Action(CaptureCommand.RepeatRegion, "Repeat last region", Glyph.Repeat),
            Action(CaptureCommand.Scroll, "Scrolling capture", Glyph.Scroll),
            Action(CaptureCommand.ScrollHorizontal, "Horizontal scrolling", Glyph.HorizontalScroll),
            Action(CaptureCommand.Text, "Capture text (OCR)", Glyph.Scan));
        var quickAccess = Action(CaptureCommand.Recent, "Quick Access", Glyph.Image, 4);
        var studio = Action(CaptureCommand.OpenStudio, "Open video in Studio", Glyph.Studio);
        var history = Action(CaptureCommand.History, "Capture history", Glyph.History);
        MenuSection[] sections = compact
            ? [capture with { Actions = [.. capture.Actions.Where(a => a.Label != "Horizontal scrolling")] },
                new("Workspace", quickAccess, studio)]
            : [
                capture,
                new("Workspace", Action(CaptureCommand.Toolbar, "Capture toolbar", Glyph.Mark, 3), quickAccess, history, studio),
                new("Record",
                    Action(CaptureCommand.RecordRegion, "Record region", Glyph.Video),
                    Action(CaptureCommand.RecordDisplay, "Record display", Glyph.Display)),
                new(null, Action(CaptureCommand.Exit, "Quit REAL Capture X", Glyph.Power))
            ];
        var header = compact ? null : new MenuHeader("REAL Capture X",
            new MenuAction("Settings", Glyph.Gear, () => execute(CaptureCommand.Settings)),
            new MenuAction("Capture history", Glyph.History, () => execute(CaptureCommand.History)));
        Title = compact ? "REAL Capture X — Quick menu" : "REAL Capture X — Commands";
        // A point anchor (right click) opens to the right of the pointer like a Windows context menu;
        // a control anchor (tray icon, … button) lines the menu up with the control's far edge.
        var point = anchor.Width <= 1 && anchor.Height <= 1;
        ShowMenu(anchor, header, sections, compact ? 300 : 320, alignLeft: point);
    }

    /// <summary>Opens any command list next to an anchor (physical screen pixels).</summary>
    public void ShowMenu(NativeWindow.Rect anchor, MenuHeader? header, MenuSection[] sections, int width = 280,
        bool alignLeft = false, Action? onClosed = null)
    {
        closed?.Invoke();
        closed = onClosed;
        revision++;
        dismissing = false;
        glass.Panel.IsHitTestVisible = true;
        host.Children.Clear();
        var content = new CaptureMenu(header, sections, run =>
        {
            HideNow();
            run();
        });
        host.Children.Add(content);
        content.Measure(new Windows.Foundation.Size(width - 2, double.PositiveInfinity));
        var height = (int)Math.Ceiling(content.DesiredSize.Height) + 2;
        var (above, origin) = NativeWindow.PlacePopup(this, anchor, GlassSurface.Outer(width), GlassSurface.Outer(height),
            GlassSurface.Margin, alignLeft);
        exitDrop = above ? 4 : -4;
        scroller.ChangeView(null, 0, null, disableAnimation: true);
        Activate();
        NativeWindow.SetForegroundWindow(NativeWindow.Handle(this));
        // Grows out of the control that opened it (scale from the anchor side, a short travel away from it);
        // the rows arrive with the panel instead of cascading, so the menu is usable on its first frame.
        glass.Enter(rise: above ? 6 : -6, fromScale: .94f, spec: SpringSpec.Snappy, origin: origin);
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
        glass.Conceal();
        var callback = closed;
        closed = null;
        callback?.Invoke();
    }

    private async Task DismissAsync()
    {
        if (dismissing || !AppWindow.IsVisible) return;
        dismissing = true;
        var current = ++revision;
        glass.Panel.IsHitTestVisible = false;
        await glass.ExitAsync(drop: exitDrop, toScale: .96f);
        if (current == revision) HideNow();
    }
}
