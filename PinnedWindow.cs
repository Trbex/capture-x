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
    private readonly VelocityTracker velocity = new();
    private SpringDriver? flight;
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

        // Direct manipulation: the image stays glued to the point that was grabbed (even mid-flight), resists
        // progressively past the outer edges of the desktop, and on release is thrown with the pointer's velocity
        // to where that momentum would carry it, then settles inside the display it lands on.
        picture.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(picture).Properties.IsLeftButtonPressed || !picture.CapturePointer(e.Pointer)) return;
            flight?.Stop();
            NativeWindow.GetCursorPos(out var cursor);
            drag = (cursor, AppWindow.Position);
            velocity.Reset();
            velocity.Add(cursor.X, cursor.Y);
        };
        picture.PointerMoved += (_, _) =>
        {
            if (drag is not { } start) return;
            NativeWindow.GetCursorPos(out var cursor);
            velocity.Add(cursor.X, cursor.Y);
            var wanted = new PointInt32(start.Window.X + cursor.X - start.Pointer.X, start.Window.Y + cursor.Y - start.Pointer.Y);
            AppWindow.Move(Resist(wanted));
        };
        picture.PointerReleased += (_, e) =>
        {
            if (drag is null) return;
            drag = null;
            picture.ReleasePointerCapture(e.Pointer);
            Throw();
        };
        picture.PointerCaptureLost += (_, _) =>
        {
            if (drag is null) return;
            drag = null;
            Throw();
        };
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
            flight?.Stop();
            AppWindow.Move(pos);
            e.Handled = true;
        };
        Closed += (_, _) =>
        {
            closed = true;
            flight?.Stop();
        };

        static Button Overlay(Glyph glyph, string name, Action? run)
        {
            var button = Appearance.IconButton(glyph, name, run, 28);
            button.Background = Appearance.Brush(12, 12, 14, 170);
            button.CornerRadius = new CornerRadius(14);
            button.Content = Symbols.Draw(glyph, 14, Appearance.Brush(255, 255, 255));
            return button;
        }
    }

    private int Inset() => (int)Math.Round(GlassSurface.Margin * Math.Max(96, NativeWindow.GetDpiForWindow(NativeWindow.Handle(this))) / 96.0);

    /// <summary>Rubber-bands a window position whose visible panel crosses an edge with no display beyond it.</summary>
    private PointInt32 Resist(PointInt32 position)
    {
        var inset = Inset();
        var size = AppWindow.Size;
        var panel = new NativeWindow.Rect
        {
            Left = position.X + inset, Top = position.Y + inset,
            Right = position.X + size.Width - inset, Bottom = position.Y + size.Height - inset
        };
        var centre = new NativeWindow.Point { X = (panel.Left + panel.Right) / 2, Y = (panel.Top + panel.Bottom) / 2 };
        var bounds = NativeWindow.GetMonitor(NativeWindow.MonitorFromPoint(centre, 2)).Bounds;
        double x = position.X, y = position.Y;
        if (panel.Left < bounds.Left && Outside(bounds.Left - 1, centre.Y))
            x += bounds.Left - panel.Left - SpringMath.Rubberband(bounds.Left - panel.Left, panel.Width);
        else if (panel.Right > bounds.Right && Outside(bounds.Right, centre.Y))
            x -= panel.Right - bounds.Right - SpringMath.Rubberband(panel.Right - bounds.Right, panel.Width);
        if (panel.Top < bounds.Top && Outside(centre.X, bounds.Top - 1))
            y += bounds.Top - panel.Top - SpringMath.Rubberband(bounds.Top - panel.Top, panel.Height);
        else if (panel.Bottom > bounds.Bottom && Outside(centre.X, bounds.Bottom))
            y -= panel.Bottom - bounds.Bottom - SpringMath.Rubberband(panel.Bottom - bounds.Bottom, panel.Height);
        return new PointInt32((int)Math.Round(x), (int)Math.Round(y));

        static bool Outside(int px, int py) => NativeWindow.MonitorFromPoint(new NativeWindow.Point { X = px, Y = py }, 0) == 0;
    }

    /// <summary>Projects the release velocity forward and springs the window to that resting place.</summary>
    private void Throw()
    {
        var (vx, vy) = velocity.Velocity();
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var inset = Inset();
        var landing = new NativeWindow.Point
        {
            X = (int)Math.Round(position.X + SpringMath.Project(vx) + size.Width / 2.0),
            Y = (int)Math.Round(position.Y + SpringMath.Project(vy) + size.Height / 2.0)
        };
        var work = NativeWindow.GetMonitor(NativeWindow.MonitorFromPoint(landing, 2)).Work;
        // Comes to rest a little clear of the display edge, like every other floating surface.
        var gap = (int)Math.Round(12 * Math.Max(96, NativeWindow.GetDpiForWindow(NativeWindow.Handle(this))) / 96.0);
        inset -= gap;
        var projectedX = landing.X - size.Width / 2.0;
        var projectedY = landing.Y - size.Height / 2.0;
        var x = Math.Clamp(projectedX, work.Left - inset, Math.Max(work.Left - inset, work.Right - size.Width + inset));
        var y = Math.Clamp(projectedY, work.Top - inset, Math.Max(work.Top - inset, work.Bottom - size.Height + inset));
        var bumped = Math.Abs(x - projectedX) > 1 || Math.Abs(y - projectedY) > 1;
        if (x == position.X && y == position.Y) return;
        flight ??= new SpringDriver(2, value =>
        {
            if (!closed) AppWindow.Move(new PointInt32((int)Math.Round(value[0]), (int)Math.Round(value[1])));
        }, precision: .5);
        flight.Reset([position.X, position.Y]);
        // A throw that runs into the edge of the display carries momentum, so it may overshoot a touch;
        // a throw that comes to rest on its own glides in critically damped.
        flight.To([x, y], bumped ? SpringSpec.Momentum : new SpringSpec(1f, .42f), [vx, vy]);
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
