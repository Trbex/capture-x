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
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        layout.RowDefinitions.Add(new RowDefinition());
        var bar = new Grid { ColumnSpacing = 3, Padding = new Thickness(8, 2, 4, 2) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        caption.Children.Add(Symbols.Draw(Glyph.Grip, 16, Appearance.Muted));
        caption.Children.Add(Appearance.Label("Pinned", 11, Appearance.Muted, strong: true));
        var grip = new Border { Background = Appearance.Clear, Child = caption };
        bar.Children.Add(grip);

        var sizeButton = Appearance.IconButton(Glyph.Scale, "Resize pinned image", size: 29);
        sizeButton.Flyout = CaptureMenu.Flyout(new MenuSection("Image size",
            new[] { 240, 360, 540, 720 }.Select(size =>
                new MenuAction($"Fit to {size} px", Glyph.Scale, () => Resize(size, center: false))).ToArray()));
        Grid.SetColumn(sizeButton, 1);
        bar.Children.Add(sizeButton);
        var close = Appearance.IconButton(Glyph.Close, "Unpin screenshot", Close, 29);
        Grid.SetColumn(close, 2);
        bar.Children.Add(close);
        layout.Children.Add(bar);

        var stage = new Border
        {
            Background = Appearance.Canvas, CornerRadius = new CornerRadius(8),
            Margin = new Thickness(4, 0, 4, 4), Child = preview
        };
        Grid.SetRow(stage, 1);
        layout.Children.Add(stage);
        Appearance.SetContent(this, Appearance.Frame(layout), floating: true);

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
        ProductMotion.Enter((FrameworkElement)Content);
        AppWindow.Show(false);
    }

    private void Resize(int size, bool center)
    {
        var ratio = Math.Min(1, size / (double)Math.Max(entry.Width, entry.Height));
        var width = Math.Max(148, 10 + (int)Math.Round(entry.Width * ratio));
        var height = 42 + (int)Math.Round(entry.Height * ratio);
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
