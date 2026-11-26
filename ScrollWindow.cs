using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace RealCaptureX;

internal sealed class ScrollWindow : Window
{
    private readonly CancellationTokenSource done = new();
    private readonly TextBlock status;
    private readonly bool horizontal;
    private readonly string direction;
    private readonly GlassSurface glass;
    private bool commit;
    private bool closed;

    public ScrollWindow(bool horizontal)
    {
        this.horizontal = horizontal;
        direction = horizontal ? "Scroll right slowly" : "Scroll down slowly";
        status = Appearance.Label(direction, 12, Appearance.Muted);
        Title = "REAL Capture X — Scrolling Capture";
        NativeWindow.ToolWindow(this);
        var panel = new StackPanel { Padding = new Thickness(14, 12, 12, 12), Spacing = 11 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 11 };
        header.Children.Add(Appearance.Badge(horizontal ? Glyph.HorizontalScroll : Glyph.Scroll, 34));
        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Appearance.Label("Scrolling capture", 14, strong: true));
        title.Children.Add(status);
        header.Children.Add(title);
        panel.Children.Add(header);
        panel.Children.Add(Appearance.Rule());
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(Appearance.Action("Cancel", Close, tone: ButtonTone.Raised));
        var finish = Appearance.Action("Finish", () => { commit = true; done.Cancel(); }, Glyph.Check, ButtonTone.Primary);
        buttons.Children.Add(finish);
        panel.Children.Add(buttons);
        glass = Appearance.Float(this, panel, radius: 14);
        Closed += (_, _) => { closed = true; done.Cancel(); };
    }

    public async Task<CaptureFrame?> RunAsync(nint monitor, RectInt32 area, CaptureFrame first)
    {
        if (area.Width < 80 || area.Height < 100)
        {
            Close();
            throw new InvalidOperationException("Select a scrollable area at least 80 × 100 pixels.");
        }
        var display = NativeWindow.GetMonitor(monitor).Bounds;
        var anchorX = area.X + area.Width / 2 < display.Width / 2 ? 1 : 0;
        NativeWindow.Place(this, monitor, GlassSurface.Outer(340), GlassSurface.Outer(124), anchorX, 0, GlassSurface.Margin);
#if DEBUG
        if (!QaMode.Inspect)
#endif
            NativeWindow.ExcludeFromCapture(this);
        AppWindow.Show(false);
        glass.Enter(rise: -10);
        var cancellation = done.Token;
        var dispatcher = DispatcherQueue;
        try
        {
            return await Task.Run(async () =>
            {
                var stitcher = new ScrollStitcher(horizontal ? first.Transpose() : first);
                using var feed = new GraphicsCapture.Feed(monitor, window: false, cursor: false);
                try
                {
                    while (!stitcher.AtLimit)
                    {
                        using (var frame = await feed.NextAsync(cancellation))
                        {
                            var current = feed.Read(frame, area);
                            if (stitcher.Append(horizontal ? current.Transpose() : current))
                            {
                                var height = stitcher.Height;
                                dispatcher.TryEnqueue(() => status.Text = $"{height:N0} px captured · {direction}");
                            }
                        }
                        await Task.Delay(110, cancellation);
                    }
                    dispatcher.TryEnqueue(() => status.Text = "Maximum image size reached");
                    await Task.Delay(Timeout.Infinite, cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                if (!commit) return null;
                var stitched = stitcher.Complete();
                return horizontal ? stitched.Transpose() : stitched;
            });
        }
        finally
        {
            if (!closed) Close();
            done.Dispose();
        }
    }
}
