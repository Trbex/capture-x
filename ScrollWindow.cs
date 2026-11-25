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
    private bool commit;
    private bool closed;

    public ScrollWindow(bool horizontal)
    {
        this.horizontal = horizontal;
        direction = horizontal ? "Scroll right slowly" : "Scroll down slowly";
        status = Appearance.Label(direction, 12, Appearance.Muted);
        Title = "REAL Capture X — Scrolling Capture";
        NativeWindow.ToolWindow(this);
        var panel = new StackPanel
        {
            RequestedTheme = ElementTheme.Dark, Background = Appearance.Surface,
            Padding = new Thickness(12, 8, 12, 8), Spacing = 5
        };
        panel.Children.Add(Appearance.Label("Scrolling capture", 14));
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var finish = new Button { Content = "Finish" };
        finish.Click += (_, _) => { commit = true; done.Cancel(); };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(finish);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
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
        NativeWindow.Place(this, monitor, 280, 112, anchorX, 0);
#if DEBUG
        if (Environment.GetEnvironmentVariable("REAL_CAPTURE_X_INSPECT") != "1")
#endif
            NativeWindow.ExcludeFromCapture(this);
        AppWindow.Show(false);
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
