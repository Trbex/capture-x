using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace RealCaptureX;

/// <summary>
/// Floating thumbnail that appears after a capture. Hovering reveals the actions; earlier captures stack
/// behind the card and can be browsed; the card itself drags into other apps as a PNG file.
/// </summary>
internal sealed class QuickAccessWindow : Window
{
    private const int CardWidth = 300;
    private readonly GlassSurface glass;
    private readonly Grid card;
    private readonly Border picture;
    private readonly ImageBrush image = new() { Stretch = Stretch.UniformToFill };
    private readonly Grid actions;
    private readonly TextBlock counter = Appearance.Label("", 11, Appearance.Text, strong: true);
    private readonly Border counterBadge;
    private readonly TextBlock feedback = Appearance.Label("", 12, Appearance.Text, strong: true);
    private readonly Border feedbackPill;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly Button previous;
    private readonly Button next;
    private CaptureEntry? entry;
    private Task<CaptureEntry>? pending;
    private double timeoutSeconds;
    private bool pointerInside;
    private bool dragging;
    private bool menuOpen;
    private bool dismissing;
    private int busy;
    private int revision;
    private int feedbackRevision;
    private double anchorX;

    public QuickAccessWindow(Func<CaptureEntry, Task> open, Func<CaptureEntry, Task> copy, Func<CaptureEntry, Task> save,
        Func<CaptureEntry, Task> pin, Func<CaptureEntry, Task> text, Action<int> browse, Action dismiss)
    {
        Title = "REAL Capture X — Quick Access";
        NativeWindow.ToolWindow(this);
        timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => dismiss();

        card = new Grid();
        picture = new Border
        {
            CornerRadius = new CornerRadius(12), Background = image, CanDrag = true
        };
        AutomationProperties.SetName(picture, "Screenshot preview");
        ToolTipService.SetToolTip(picture, "Drag into another app · Click to annotate");
        picture.DragStarting += OnDragStarting;
        picture.DropCompleted += (_, _) => { dragging = false; RestartTimer(); };
        picture.Tapped += async (_, _) => await UseAsync(open, null);
        card.Children.Add(picture);

        actions = new Grid
        {
            CornerRadius = new CornerRadius(12), Background = Appearance.Brush(10, 10, 12, 150),
            Padding = new Thickness(8)
        };
        actions.CanDrag = true;
        actions.DragStarting += OnDragStarting;
        actions.DropCompleted += (_, _) => { dragging = false; RestartTimer(); };
        actions.Tapped += async (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, actions)) await UseAsync(open, null);
        };
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition());
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Corner(Glyph.Close, "Dismiss", dismiss, 0, 0);
        Corner(Glyph.Pen, "Annotate", () => _ = UseAsync(open, null), 0, 2);
        Corner(Glyph.Pin, "Pin to screen", () => _ = UseAsync(pin, "Pinned"), 2, 0);
        var more = Corner(Glyph.More, "More actions", null, 2, 2);
        more.Click += (_, _) =>
        {
            menuOpen = true;
            timer.Stop();
            TrayMenuWindow.Shared?.ShowMenu(NativeWindow.ScreenRect(this, more), null,
            [
                new MenuSection(null,
                    new MenuAction("Annotate", Glyph.Pen, () => _ = UseAsync(open, null)),
                    new MenuAction("Pin to screen", Glyph.Pin, () => _ = UseAsync(pin, "Pinned")),
                    new MenuAction("Copy text (OCR)", Glyph.Scan, () => _ = UseAsync(text, "Text copied")),
                    new MenuAction("Save PNG as…", Glyph.Save, () => _ = UseAsync(save, null)))
            ], 230, onClosed: () => { menuOpen = false; RestartTimer(); });
        };
        var center = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        center.Children.Add(Pill("Copy", Glyph.Copy, () => _ = UseAsync(copy, "Copied")));
        center.Children.Add(Pill("Save", Glyph.Save, () => _ = UseAsync(save, null)));
        Grid.SetRow(center, 1);
        Grid.SetColumnSpan(center, 3);
        actions.Children.Add(center);
        card.Children.Add(actions);

        counterBadge = new Border
        {
            Child = counter, Padding = new Thickness(7, 2, 7, 3), CornerRadius = new CornerRadius(9),
            Background = Appearance.Brush(10, 10, 12, 170), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), IsHitTestVisible = false
        };
        card.Children.Add(counterBadge);
        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8)
        };
        previous = OverlayButton(Glyph.Back, "Older screenshot", () => browse(1), 26);
        next = OverlayButton(Glyph.Next, "Newer screenshot", () => browse(-1), 26);
        navigation.Children.Add(previous);
        navigation.Children.Add(new Border { Width = 34 });
        navigation.Children.Add(next);
        Grid.SetRow(navigation, 2);
        Grid.SetColumnSpan(navigation, 3);
        actions.Children.Add(navigation);

        feedbackPill = new Border
        {
            Padding = new Thickness(10, 5, 12, 6), CornerRadius = new CornerRadius(14),
            Background = Appearance.Brush(10, 10, 12, 200), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        var feedbackRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        feedbackRow.Children.Add(Symbols.Draw(Glyph.Check, 15, Appearance.Brush(255, 255, 255)));
        feedback.Foreground = Appearance.Brush(255, 255, 255);
        feedbackRow.Children.Add(feedback);
        feedbackPill.Child = feedbackRow;
        card.Children.Add(feedbackPill);
        ProductMotion.Hide(feedbackPill);
        ProductMotion.Hide(actions);

        glass = Appearance.Float(this, card, radius: 14);
        glass.Panel.Padding = new Thickness(5);
        glass.Panel.PointerEntered += (_, _) => { pointerInside = true; timer.Stop(); ShowActions(true); };
        glass.Panel.PointerExited += (_, _) => { pointerInside = false; ShowActions(false); RestartTimer(); };
        glass.Panel.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Escape: dismiss(); break;
                case VirtualKey.Left when previous.IsEnabled: browse(1); break;
                case VirtualKey.Right when next.IsEnabled: browse(-1); break;
                case VirtualKey.C when IsControl(): _ = UseAsync(copy, "Copied"); break;
                case VirtualKey.S when IsControl(): _ = UseAsync(save, null); break;
                case VirtualKey.Enter: _ = UseAsync(open, null); break;
                default: return;
            }
            e.Handled = true;
        };
#if DEBUG
        if (!QaMode.Inspect)
#endif
            NativeWindow.ExcludeFromCapture(this);

        Button Corner(Glyph glyph, string name, Action? run, int row, int column)
        {
            var button = OverlayButton(glyph, name, run, 30);
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            actions.Children.Add(button);
            return button;
        }

        static bool IsControl() => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    /// <summary>White-on-scrim icon button used on top of the screenshot, independent of the app theme.</summary>
    private static Button OverlayButton(Glyph glyph, string name, Action? run, double size)
    {
        var button = Appearance.IconButton(glyph, name, run, size);
        button.Background = Appearance.Brush(255, 255, 255, 34);
        button.CornerRadius = new CornerRadius(size / 2);
        button.Content = Symbols.Draw(glyph, size >= 30 ? 16 : 14, Appearance.Brush(255, 255, 255));
        button.RequestedTheme = ElementTheme.Dark;
        return button;
    }

    private static Button Pill(string label, Glyph glyph, Action run)
    {
        var button = Appearance.Action(label, run);
        var ink = Appearance.Brush(255, 255, 255);
        button.Content = Appearance.ActionContent(label, glyph, ink);
        button.Foreground = ink;
        button.Background = Appearance.Brush(255, 255, 255, 40);
        button.BorderBrush = Appearance.Brush(255, 255, 255, 50);
        button.BorderThickness = new Thickness(1);
        button.Height = 34;
        button.Padding = new Thickness(14, 0, 16, 0);
        button.CornerRadius = new CornerRadius(17);
        button.RequestedTheme = ElementTheme.Dark;
        return button;
    }

    private void ShowActions(bool visible)
    {
        actions.IsHitTestVisible = visible;
        ProductMotion.Opacity(actions, visible ? 1 : 0, visible ? ProductMotion.Hover : TimeSpan.FromMilliseconds(180));
        ProductMotion.Opacity(counterBadge, visible ? 0 : 1, ProductMotion.Hover);
    }

    public async Task DisplayAsync(CaptureEntry capture, nint monitor, int index, int count,
        double seconds, double anchorX, double anchorY, bool animate)
    {
        var current = ++revision;
        var source = new BitmapImage();
        if (capture.Width >= capture.Height) source.DecodePixelWidth = Math.Min(640, capture.Width);
        else source.DecodePixelHeight = Math.Min(560, capture.Height);
        using (var stream = await (await StorageFile.GetFileFromPathAsync(capture.Path)).OpenReadAsync())
            await source.SetSourceAsync(stream);
        if (current != revision) return;
        pending = null;
        Bind(capture, index);
        Present(source, capture.Width, capture.Height, monitor, index, count, seconds, anchorX, anchorY, animate);
    }

    /// <summary>
    /// Shows a capture that is still being written to disk: the preview comes straight from the captured pixels,
    /// so the card is on screen within a frame or two of the capture instead of after PNG encoding and a reload.
    /// Actions wait for the saved file transparently.
    /// </summary>
    public async Task DisplayFreshAsync(CaptureFrame frame, Task<CaptureEntry> saving, nint monitor, int count,
        double seconds, double anchorX, double anchorY)
    {
        var current = ++revision;
        var (width, height, pixels) = await Task.Run(() => Thumbnail(frame, 640, 560));
        if (current != revision) return;
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels);
        bitmap.Invalidate();
        entry = null;
        pending = saving;
        AutomationProperties.SetHelpText(picture, $"Screenshot, {frame.Width} × {frame.Height} px");
        Present(bitmap, frame.Width, frame.Height, monitor, 0, count, seconds, anchorX, anchorY, animate: true);
        previous.IsEnabled = next.IsEnabled = false;
        try
        {
            var saved = await saving;
            if (current == revision && ReferenceEquals(pending, saving)) Bind(saved, 0);
        }
        catch
        {
            // The controller reports the failed save; the card simply stays without actions.
        }
        finally
        {
            if (current == revision) previous.IsEnabled = count > 1;
        }
    }

    private void Bind(CaptureEntry capture, int index)
    {
        entry = capture;
        entryIndex = index;
        AutomationProperties.SetHelpText(picture, $"{CaptureLabel.Of(capture.Mode)}, {capture.Width} × {capture.Height} px");
    }

    /// <summary>Box-filtered downscale of the captured pixels on a worker thread (premultiplied BGRA, opaque).</summary>
    private static (int Width, int Height, byte[] Pixels) Thumbnail(CaptureFrame frame, int maxWidth, int maxHeight)
    {
        var factor = Math.Max(1, (int)Math.Ceiling(Math.Max(frame.Width / (double)maxWidth, frame.Height / (double)maxHeight)));
        var width = Math.Max(1, frame.Width / factor);
        var height = Math.Max(1, frame.Height / factor);
        var output = new byte[width * height * 4];
        if (factor == 1)
        {
            // Small captures are shown as they are; only the (ignored) alpha channel is forced opaque.
            var from = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frame.Bgra.AsSpan());
            var to = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(output.AsSpan());
            for (var i = 0; i < to.Length; i++) to[i] = from[i] | 0xFF000000;
            return (width, height, output);
        }
        var source = frame.Bgra;
        var area = factor * factor;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                int b = 0, g = 0, r = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    var row = ((y * factor + dy) * frame.Width + x * factor) * 4;
                    for (var dx = 0; dx < factor; dx++, row += 4)
                    {
                        b += source[row];
                        g += source[row + 1];
                        r += source[row + 2];
                    }
                }
                var o = (y * width + x) * 4;
                output[o] = (byte)(b / area);
                output[o + 1] = (byte)(g / area);
                output[o + 2] = (byte)(r / area);
                output[o + 3] = 255;
            }
        return (width, height, output);
    }

    private void Present(ImageSource source, int pixelWidth, int pixelHeight, nint monitor, int index, int count,
        double seconds, double anchorX, double anchorY, bool animate)
    {
        dismissing = false;
        glass.Panel.IsHitTestVisible = true;
        timer.Stop();
        var direction = index > (shownIndex ?? index) ? 1 : -1;
        shownIndex = index;
        image.ImageSource = source;
        counter.Text = count > 1 ? $"{index + 1} / {Math.Min(count, 999)}" : "";
        counterBadge.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        previous.Visibility = next.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        previous.IsEnabled = index + 1 < count;
        next.IsEnabled = index > 0;
        timeoutSeconds = seconds;
        this.anchorX = anchorX;

        var visible = AppWindow.IsVisible;
        if (!visible || animate)
        {
            var ratio = pixelHeight / (double)Math.Max(1, pixelWidth);
            var height = (int)Math.Clamp((CardWidth - 10) * ratio, 120, 300) + 10;
            NativeWindow.Place(this, monitor, GlassSurface.Outer(CardWidth), GlassSurface.Outer(height), anchorX, anchorY,
                GlassSurface.Margin);
            NativeWindow.SetWindowPos(NativeWindow.Handle(this), NativeWindow.Topmost, 0, 0, 0, 0, NativeWindow.NoActivate | 0x0003);
        }
        AppWindow.Show(false);
        if (animate && !visible)
        {
            // Emerges from the screen edge it lives on (the window margin sits just past that edge, so the card
            // is clipped there) and later leaves the same way.
            // It is launched already moving, so it is visible on its first frame instead of creeping out from behind the edge.
            glass.Enter(slide: EdgeTravel, fromScale: 1, spec: Arrive, origin: new Vector2(anchorX < .5 ? 0 : 1, .5f),
                velocity: ProductMotion.Launch(new Vector2(EdgeTravel, 0), Arrive));
        }
        else if (visible)
        {
            glass.Settle(Arrive);
            if (!animate) ProductMotion.Reveal(picture, slide: direction * 18);
        }
        ShowActions(pointerInside);
        RestartTimer();
    }

    /// <summary>The card glides in from its screen edge: critically damped, a little slower than a popup because it travels further.</summary>
    private static readonly SpringSpec Arrive = new(1f, .36f);

    /// <summary>Horizontal distance that puts the whole card just beyond its screen edge.</summary>
    private float EdgeTravel => (float)((CardWidth + 24) * (anchorX < .5 ? -1 : 1));

    private int? entryIndex;
    private int? shownIndex;

    public void HideNow()
    {
        revision++;
        dismissing = false;
        timer.Stop();
        pointerInside = false;
        entryIndex = null;
        shownIndex = null;
        AppWindow.Hide();
        glass.Conceal();
    }

    public async Task DismissAsync()
    {
        if (!AppWindow.IsVisible || dismissing) return;
        var current = ++revision;
        dismissing = true;
        timer.Stop();
        glass.Panel.IsHitTestVisible = false;
        await glass.ExitAsync(slide: EdgeTravel, toScale: 1);
        if (current == revision) HideNow();
    }

    private void RestartTimer()
    {
        timer.Stop();
        if (pointerInside || dragging || menuOpen || dismissing || busy > 0 || timeoutSeconds <= 0 || !AppWindow.IsVisible) return;
        timer.Interval = TimeSpan.FromSeconds(timeoutSeconds);
        timer.Start();
    }

    private void Flash(string message)
    {
        var current = ++feedbackRevision;
        feedback.Text = message;
        ProductMotion.Enter(feedbackPill, fromScale: .9f);
        _ = Task.Delay(1100).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (current == feedbackRevision) ProductMotion.Opacity(feedbackPill, 0, TimeSpan.FromMilliseconds(220));
        }));
    }

    private async Task UseAsync(Func<CaptureEntry, Task> action, string? done)
    {
        var current = revision;
        busy++;
        timer.Stop();
        try
        {
            var capture = entry ?? (pending is { } saving ? await saving : null);
            if (capture is null || revision != current) return;
            await action(capture);
            if (done is not null && revision == current) Flash(done);
        }
        catch (Exception ex)
        {
            if (revision == current) ToastWindow.Show("Action failed", ex.Message, Glyph.Close);
        }
        finally
        {
            busy--;
            RestartTimer();
        }
    }

    private async void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (entry is null && pending is null)
        {
            args.Cancel = true;
            return;
        }
        var deferral = args.GetDeferral();
        dragging = true;
        timer.Stop();
        try
        {
            var capture = entry ?? await pending!;
            var file = await StorageFile.GetFileFromPathAsync(capture.Path);
            args.AllowedOperations = DataPackageOperation.Copy;
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.Data.SetStorageItems([file], readOnly: true);
            args.DragUI.SetContentFromDataPackage();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            dragging = false;
            ToastWindow.Show("Drag failed", ex.Message, Glyph.Close);
            RestartTimer();
        }
        finally
        {
            deferral.Complete();
        }
    }
}

internal static class CaptureLabel
{
    internal static string Of(string mode) => mode switch
    {
        "Region" => "Region screenshot",
        "Window" => "Window screenshot",
        "Display" => "Display screenshot",
        "Scroll" => "Scrolling screenshot",
        "ScrollHorizontal" => "Wide scrolling screenshot",
        "Edited" => "Edited screenshot",
        _ => "Screenshot"
    };
}
