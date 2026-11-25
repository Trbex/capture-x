using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace RealCaptureX;

internal sealed class PinnedWindow : Window
{
    private readonly CaptureEntry entry;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private (NativeWindow.Point Pointer, PointInt32 Window)? drag;
    private bool closed;

    public PinnedWindow(CaptureEntry capture)
    {
        entry = capture;
        Title = "REAL Capture X — Pinned";
        NativeWindow.ToolWindow(this);
        var layout = new Grid { Background = Appearance.Surface, RequestedTheme = ElementTheme.Dark };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        var bar = new Grid { ColumnSpacing = 6, Padding = new Thickness(8, 3, 5, 3) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = new Border
        {
            Background = Appearance.Brush(0, 0, 0, 0),
            Child = Appearance.Label("Pinned", 11, Appearance.Muted),
            Padding = new Thickness(3, 0, 3, 0)
        };
        bar.Children.Add(grip);
        var sizes = new MenuFlyout();
        foreach (var size in new[] { 240, 360, 540, 720 })
        {
            var item = new MenuFlyoutItem { Text = $"Fit to {size} px" };
            item.Click += (_, _) => Resize(size, center: false);
            sizes.Items.Add(item);
        }
        var sizeButton = Appearance.IconButton("\uE740", "Resize pinned image");
        sizeButton.Flyout = sizes;
        Grid.SetColumn(sizeButton, 1);
        bar.Children.Add(sizeButton);
        var close = Appearance.IconButton("\uE711", "Unpin screenshot", Close);
        Grid.SetColumn(close, 2);
        bar.Children.Add(close);
        layout.Children.Add(bar);
        Grid.SetRow(preview, 1);
        layout.Children.Add(preview);
        Content = layout;

        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || !grip.CapturePointer(e.Pointer)) return;
            NativeWindow.GetCursorPos(out var cursor);
            drag = (cursor, AppWindow.Position);
        };
        grip.PointerMoved += (_, _) =>
        {
            if (drag is not { } start) return;
            NativeWindow.GetCursorPos(out var cursor);
            AppWindow.Move(new PointInt32(start.Window.X + cursor.X - start.Pointer.X,
                start.Window.Y + cursor.Y - start.Pointer.Y));
        };
        grip.PointerReleased += (_, e) => { drag = null; grip.ReleasePointerCapture(e.Pointer); };
        grip.PointerCaptureLost += (_, _) => drag = null;
        layout.KeyDown += (_, e) =>
        {
            var pos = AppWindow.Position;
            switch (e.Key)
            {
                case VirtualKey.Left: pos.X--; break;
                case VirtualKey.Right: pos.X++; break;
                case VirtualKey.Up: pos.Y--; break;
                case VirtualKey.Down: pos.Y++; break;
                case VirtualKey.Escape: Close(); return;
                default: return;
            }
            AppWindow.Move(pos);
            e.Handled = true;
        };
        Closed += (_, _) => closed = true;
    }

    public async Task ShowAsync()
    {
        var source = new BitmapImage { DecodePixelWidth = Math.Min(1440, entry.Width) };
        using (var stream = await (await StorageFile.GetFileFromPathAsync(entry.Path)).OpenReadAsync())
            await source.SetSourceAsync(stream);
        if (closed) return;
        preview.Source = source;
        Resize(360, center: true);
        AppWindow.Show(false);
    }

    private void Resize(int size, bool center)
    {
        var ratio = Math.Min(1, size / (double)Math.Max(entry.Width, entry.Height));
        var width = Math.Max(110, (int)Math.Round(entry.Width * ratio));
        var height = 37 + (int)Math.Round(entry.Height * ratio);
        var monitor = NativeWindow.CursorMonitor();
        if (center)
            NativeWindow.Place(this, monitor, width, height, 0.75, 0.27);
        else
        {
            var scale = Math.Max(96, NativeWindow.GetDpiForWindow(NativeWindow.Handle(this))) / 96.0;
            AppWindow.Resize(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        }
    }
}
