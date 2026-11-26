using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace RealCaptureX;

/// <summary>A screenshot floating above other windows. Drag anywhere to move; controls appear on hover.</summary>
internal sealed class PinnedWindow : Window
{
    private readonly CaptureEntry entry;
    private readonly ImageBrush image = new() { Stretch = Stretch.Uniform };
    private readonly GlassSurface glass;
    private readonly Grid controls;
    private (NativeWindow.Point Pointer, PointInt32 Window)? drag;
    private bool closed;

    public PinnedWindow(CaptureEntry capture)
    {
        entry = capture;
        Title = "REAL Capture X — Pinned";
        NativeWindow.ToolWindow(this);
        var layout = new Grid();
        var picture = new Border { CornerRadius = new CornerRadius(10), Background = image };
        layout.Children.Add(picture);

        controls = new Grid
        {
            VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(6), ColumnSpacing = 4,
            RequestedTheme = ElementTheme.Dark
        };
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sizeButton = Overlay(Glyph.Scale, "Resize pinned image", null);
        sizeButton.Click += (_, _) => TrayMenuWindow.Shared?.ShowMenu(NativeWindow.ScreenRect(this, sizeButton), null,
            [new MenuSection("Image size", new[] { 240, 360, 540, 720 }.Select(size =>
                new MenuAction($"Fit to {size} px", Glyph.Scale, () => Resize(size, center: false))).ToArray())], 220);
        Grid.SetColumn(sizeButton, 1);
        controls.Children.Add(sizeButton);
        var close = Overlay(Glyph.Close, "Unpin screenshot", Close);
        Grid.SetColumn(close, 2);
        controls.Children.Add(close);
        layout.Children.Add(controls);
        ProductMotion.Hide(controls);

        glass = Appearance.Float(this, layout, radius: 12);
        glass.Panel.Padding = new Thickness(3);
        glass.Panel.PointerEntered += (_, _) => ProductMotion.Opacity(controls, 1, ProductMotion.Hover);
        glass.Panel.PointerExited += (_, _) => ProductMotion.Opacity(controls, 0, TimeSpan.FromMilliseconds(200));

        picture.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(picture).Properties.IsLeftButtonPressed || !picture.CapturePointer(e.Pointer)) return;
            NativeWindow.GetCursorPos(out var cursor);
            drag = (cursor, AppWindow.Position);
        };
        picture.PointerMoved += (_, _) =>
        {
            if (drag is not { } start) return;
            NativeWindow.GetCursorPos(out var cursor);
            AppWindow.Move(new PointInt32(start.Window.X + cursor.X - start.Pointer.X,
                start.Window.Y + cursor.Y - start.Pointer.Y));
        };
        picture.PointerReleased += (_, e) => { drag = null; picture.ReleasePointerCapture(e.Pointer); };
        picture.PointerCaptureLost += (_, _) => drag = null;
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

        static Button Overlay(Glyph glyph, string name, Action? run)
        {
            var button = Appearance.IconButton(glyph, name, run, 28);
            button.Background = Appearance.Brush(12, 12, 14, 170);
            button.CornerRadius = new CornerRadius(14);
            button.Content = Symbols.Draw(glyph, 14, Appearance.Brush(255, 255, 255));
            return button;
        }
    }

    public async Task ShowAsync()
    {
        var source = new BitmapImage { DecodePixelWidth = Math.Min(1440, entry.Width) };
        using (var stream = await (await StorageFile.GetFileFromPathAsync(entry.Path)).OpenReadAsync())
            await source.SetSourceAsync(stream);
        if (closed) return;
        image.ImageSource = source;
        Resize(360, center: true);
        AppWindow.Show(false);
        glass.Enter();
    }

    private void Resize(int size, bool center)
    {
        var ratio = Math.Min(1, size / (double)Math.Max(entry.Width, entry.Height));
        var width = GlassSurface.Outer(Math.Max(120, 6 + Math.Round(entry.Width * ratio)));
        var height = GlassSurface.Outer(Math.Max(60, 6 + Math.Round(entry.Height * ratio)));
        var monitor = NativeWindow.CursorMonitor();
        if (center)
            NativeWindow.Place(this, monitor, width, height, 0.75, 0.27, GlassSurface.Margin);
        else
        {
            var scale = Math.Max(96, NativeWindow.GetDpiForWindow(NativeWindow.Handle(this))) / 96.0;
            AppWindow.Resize(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
            ProductMotion.Reveal(glass.Panel);
        }
    }
}
