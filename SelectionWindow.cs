using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
using System.Numerics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using Point = Windows.Foundation.Point;
using Windows.Graphics;
using Windows.System;
using static RealCaptureX.NativeWindow;

namespace RealCaptureX;

internal sealed class SelectionWindow : Window
{
    internal sealed record Selection(RectInt32 Area, nint Window);

    private readonly TaskCompletionSource<Selection?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CaptureFrame frame;
    private readonly NativeWindow.Rect display;
    private readonly IReadOnlyList<Target>? windows;
    private readonly Grid surface;
    private readonly Canvas canvas = new() { IsHitTestVisible = false };
    private readonly Rectangle[] shade = new Rectangle[4];
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush Ink = Appearance.Brush(255, 255, 255);
    private readonly Rectangle edgeShadow = new() { Stroke = Appearance.Brush(0, 0, 0, 120), StrokeThickness = 3, IsHitTestVisible = false };
    private readonly Rectangle outline = new() { Stroke = Ink, StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Rectangle horizontalGuide = Guide();
    private readonly Rectangle verticalGuide = Guide();
    private readonly Rectangle[] corners = Enumerable.Range(0, 8)
        .Select(_ => new Rectangle { Fill = Ink, IsHitTestVisible = false, RadiusX = 1, RadiusY = 1 }).ToArray();

    private static Rectangle Guide() =>
        new() { Fill = Appearance.Brush(255, 255, 255, 64), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border badge;
    private readonly TextBlock dimensions = Appearance.Label("", 12, Ink, strong: true);
    private readonly CanvasControl loupe = new() { Width = 124, Height = 124, IsHitTestVisible = false };
    private readonly Border loupeFrame;
    private readonly TextBlock loupeLabel = Appearance.Label("", 11, Ink, strong: true);
    private CanvasBitmap? pixels;
    private Windows.Foundation.Point pointer;
    private RectInt32 shown;
    private long tween;
    private readonly Image image = new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill, IsHitTestVisible = false };
    private NativeWindow.Point? start;
    private RectInt32 area;
    private Target? hovered;

    public SelectionWindow(CaptureFrame frame, NativeWindow.Rect display, IReadOnlyList<Target>? windows)
    {
        this.frame = frame;
        this.display = display;
        this.windows = windows;
        surface = new SelectionSurface(windows is null)
        {
            IsTabStop = true, Background = Appearance.Canvas
        };
        Title = windows is null ? "REAL Capture X — Select region" : "REAL Capture X — Select window";
        ToolWindow(this, rounded: false);

        surface.Children.Add(image);
        surface.Children.Add(canvas);
        for (var i = 0; i < shade.Length; i++)
        {
            shade[i] = new Rectangle { Fill = Appearance.Brush(8, 8, 10, 120), IsHitTestVisible = false };
            canvas.Children.Add(shade[i]);
        }
        canvas.Children.Add(horizontalGuide);
        canvas.Children.Add(verticalGuide);
        canvas.Children.Add(edgeShadow);
        canvas.Children.Add(outline);
        foreach (var corner in corners) canvas.Children.Add(corner);
        loupe.Draw += DrawLoupe;
        var loupeBody = new StackPanel { Spacing = 0 };
        loupeBody.Children.Add(new Border { Width = 124, Height = 124, CornerRadius = new CornerRadius(62), Child = loupe });
        loupeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        loupeLabel.Margin = new Thickness(0, 6, 0, 0);
        loupeBody.Children.Add(new Border
        {
            Child = loupeLabel, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 6, 0, 0), Background = Appearance.Brush(16, 16, 18, 225)
        });
        loupeFrame = new Border { Child = loupeBody, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        canvas.Children.Add(loupeFrame);
        badge = MakeBadge(dimensions);
        badge.Visibility = Visibility.Collapsed;
        badge.Padding = new Thickness(8, 4, 8, 4);
        canvas.Children.Add(badge);
        var hints = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var soft = Appearance.Brush(255, 255, 255, 150);
        hints.Children.Add(Symbols.Draw(windows is null ? Glyph.Region : Glyph.Window, 17, Ink));
        hints.Children.Add(Appearance.Label(windows is null ? "Drag to capture a region" : "Click a window to capture", 12, Ink, strong: true));
        hints.Children.Add(new Border { Width = 1, Height = 16, Background = Appearance.Brush(255, 255, 255, 40), Margin = new Thickness(4, 0, 4, 0) });
        if (windows is null)
        {
            hints.Children.Add(Cap("Shift"));
            hints.Children.Add(Appearance.Label("Square", 11, soft));
        }
        hints.Children.Add(Cap("Esc"));
        hints.Children.Add(Appearance.Label("Cancel", 11, soft));

        Border Cap(string key) => new()
        {
            Child = Appearance.Label(key, 11, Ink), Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(5),
            Background = Appearance.Brush(255, 255, 255, 26), BorderBrush = Appearance.Brush(255, 255, 255, 36),
            BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center
        };
        var instruction = MakeBadge(hints);
        instruction.HorizontalAlignment = HorizontalAlignment.Center;
        instruction.VerticalAlignment = VerticalAlignment.Top;
        instruction.Margin = new Thickness(0, 23, 0, 0);
        instruction.IsHitTestVisible = false;
        instruction.Loaded += (_, _) => ProductMotion.Enter(instruction);
        surface.Children.Add(instruction);
        Appearance.SetContent(this, surface);

        surface.PointerMoved += OnPointerMoved;
        surface.PointerPressed += OnPointerPressed;
        surface.PointerReleased += OnPointerReleased;
        surface.PointerCaptureLost += (_, _) =>
        {
            if (start is null) return;
            start = null;
            area = default;
            UpdateMask(default);
        };
        canvas.SizeChanged += (_, _) => UpdateMask(area);
        surface.Loaded += (_, _) => surface.Focus(FocusState.Programmatic);
        surface.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Finish(null);
            }
        };
        Closed += (_, _) =>
        {
            tween++;
            result.TrySetResult(null);
            loupe.RemoveFromVisualTree();
            pixels?.Dispose();
            pixels = null;
        };
    }

    private static Border MakeBadge(FrameworkElement content) => new()
    {
        Child = content, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7),
        Background = Appearance.Brush(18, 18, 20, 228), BorderBrush = Appearance.Brush(255, 255, 255, 30),
        BorderThickness = new Thickness(1), IsHitTestVisible = false, MaxWidth = 420, RequestedTheme = ElementTheme.Dark
    };

    private void DrawLoupe(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (pixels is null) return;
        const float zoom = 9;
        var ds = args.DrawingSession;
        var px = ToPixel(pointer);
        var size = (float)sender.ActualWidth;
        var span = size / zoom;
        var source = new Windows.Foundation.Rect(px.X + .5 - span / 2, px.Y + .5 - span / 2, span, span);
        ds.Clear(Microsoft.UI.Colors.Black);
        ds.DrawImage(pixels, new Windows.Foundation.Rect(0, 0, size, size), source, 1, CanvasImageInterpolation.NearestNeighbor);
        var cell = new Windows.Foundation.Rect(size / 2 - zoom / 2, size / 2 - zoom / 2, zoom, zoom);
        ds.DrawRectangle(cell, Microsoft.UI.Colors.Black, 2.5f);
        ds.DrawRectangle(cell, Microsoft.UI.Colors.White, 1);
        ds.DrawCircle(new Vector2(size / 2), size / 2 - 1, Windows.UI.Color.FromArgb(200, 255, 255, 255), 2);
    }

    private void UpdateLoupe(Windows.Foundation.Point position, bool visible)
    {
        pointer = position;
        loupeFrame.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var px = ToPixel(position);
        var color = PixelAt(px.X, px.Y);
        loupeLabel.Text = $"{px.X}, {px.Y}   #{color.R:X2}{color.G:X2}{color.B:X2}";
        var x = position.X + 22;
        var y = position.Y + 22;
        if (x + 140 > canvas.ActualWidth) x = position.X - 146;
        if (y + 160 > canvas.ActualHeight) y = position.Y - 172;
        Canvas.SetLeft(loupeFrame, x);
        Canvas.SetTop(loupeFrame, y);
        loupe.Invalidate();
    }

    private Windows.UI.Color PixelAt(int x, int y)
    {
        x = Math.Clamp(x, 0, frame.Width - 1);
        y = Math.Clamp(y, 0, frame.Height - 1);
        var i = (y * frame.Width + x) * 4;
        return Windows.UI.Color.FromArgb(255, frame.Bgra[i + 2], frame.Bgra[i + 1], frame.Bgra[i]);
    }

    public Task<Selection?> PickAsync()
    {
        var source = new WriteableBitmap(frame.Width, frame.Height);
        using (var stream = source.PixelBuffer.AsStream())
            stream.Write(frame.Bgra);
        source.Invalidate();
        image.Source = source;
        pixels = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), frame.Bgra, frame.Width, frame.Height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 96);
        ProductMotion.Hide(canvas);
        var hwnd = Handle(this);
        AppWindow.MoveAndResize(new RectInt32(display.Left, display.Top, display.Width, display.Height));
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        Activate();
        SetWindowPos(hwnd, Topmost, 0, 0, 0, 0, NoActivate | 0x0003);
        SetForegroundWindow(hwnd);
        surface.Focus(FocusState.Programmatic);
        ProductMotion.Opacity(canvas, 1, TimeSpan.FromMilliseconds(160));
        return result.Task;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(canvas).Position;
        if (windows is not null)
        {
            UpdateWindow();
            return;
        }
        UpdateLoupe(position, true);
        if (start is not { } origin)
        {
            horizontalGuide.Visibility = verticalGuide.Visibility = Visibility.Visible;
            Place(horizontalGuide, 0, position.Y, canvas.ActualWidth, 1);
            Place(verticalGuide, position.X, 0, 1, canvas.ActualHeight);
            return;
        }
        horizontalGuide.Visibility = verticalGuide.Visibility = Visibility.Collapsed;
        var a = origin;
        var b = ToPixel(position);
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            var side = Math.Min(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            b.X = a.X + Math.Sign(b.X - a.X) * side;
            b.Y = a.Y + Math.Sign(b.Y - a.Y) * side;
        }
        area = new RectInt32(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        UpdateMask(area);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(canvas);
        if (!pointer.Properties.IsLeftButtonPressed) return;
        if (windows is not null)
        {
            UpdateWindow();
            if (hovered is { } target)
            {
                ShowWindow(Handle(this), Hide);
                var actual = WindowUnderCursor();
                target = windows.FirstOrDefault(candidate => candidate.Handle == actual) ?? target;
                Finish(new Selection(default, target.Handle));
            }
            return;
        }
        surface.CapturePointer(e.Pointer);
        horizontalGuide.Visibility = verticalGuide.Visibility = Visibility.Collapsed;
        start = ToPixel(pointer.Position);
        area = default;
        UpdateMask(area);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (start is null) return;
        OnPointerMoved(sender, e);
        start = null;
        surface.ReleasePointerCapture(e.Pointer);
        if (area.Width >= 2 && area.Height >= 2)
            Finish(new Selection(area, 0));
    }

    private void UpdateWindow()
    {
        GetCursorPos(out var screen);
        hovered = windows?.FirstOrDefault(candidate => candidate.Bounds.Contains(screen));
        area = default;
        if (hovered is { Bounds: var bounds })
        {
            var left = Math.Clamp(bounds.Left - display.Left, 0, display.Width);
            var top = Math.Clamp(bounds.Top - display.Top, 0, display.Height);
            var right = Math.Clamp(bounds.Right - display.Left, 0, display.Width);
            var bottom = Math.Clamp(bounds.Bottom - display.Top, 0, display.Height);
            area = new RectInt32(left * frame.Width / display.Width, top * frame.Height / display.Height,
                (right - left) * frame.Width / display.Width, (bottom - top) * frame.Height / display.Height);
        }
        Glide(area);
    }

    /// <summary>Eases the window highlight to a new rectangle over ~140 ms on the render loop.</summary>
    private void Glide(RectInt32 target)
    {
        if (target.Equals(shown)) return;
        var from = shown;
        shown = target;
        if (!ProductMotion.Enabled || from.Width == 0 || target.Width == 0)
        {
            UpdateMask(target);
            return;
        }
        var id = ++tween;
        var started = DateTime.UtcNow;
        void Tick(object? sender, object args)
        {
            if (id != tween) { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Tick; return; }
            var t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 140, 0, 1);
            var e = 1 - Math.Pow(1 - t, 3);
            int Lerp(int a, int b) => (int)Math.Round(a + (b - a) * e);
            UpdateMask(new RectInt32(Lerp(from.X, target.X), Lerp(from.Y, target.Y), Lerp(from.Width, target.Width), Lerp(from.Height, target.Height)));
            if (t >= 1) Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Tick;
        }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Tick;
    }

    private NativeWindow.Point ToPixel(Point point) => new()
    {
        X = Math.Clamp((int)Math.Round(point.X * frame.Width / Math.Max(1, surface.ActualWidth)), 0, frame.Width),
        Y = Math.Clamp((int)Math.Round(point.Y * frame.Height / Math.Max(1, surface.ActualHeight)), 0, frame.Height)
    };

    private void UpdateMask(RectInt32 rect)
    {
        var w = canvas.ActualWidth;
        var h = canvas.ActualHeight;
        var x = rect.X * w / frame.Width;
        var y = rect.Y * h / frame.Height;
        var width = rect.Width * w / frame.Width;
        var height = rect.Height * h / frame.Height;
        Place(shade[0], 0, 0, w, y);
        Place(shade[1], 0, y, x, height);
        Place(shade[2], x + width, y, w - x - width, height);
        Place(shade[3], 0, y + height, w, h - y - height);
        var visible = width > 0 && height > 0;
        Place(edgeShadow, x, y, width, height);
        Place(outline, x, y, width, height);
        var tick = Math.Min(14, Math.Min(width, height) / 2);
        const double thick = 3;
        for (var index = 0; index < 4; index++)
        {
            var cx = x - 1 + (index % 2 == 0 ? 0 : width + 2 - thick);
            var cy = y - 1 + (index < 2 ? 0 : height + 2 - thick);
            var visibility = tick >= 4 ? Visibility.Visible : Visibility.Collapsed;
            corners[index * 2].Visibility = corners[index * 2 + 1].Visibility = visibility;
            Place(corners[index * 2], cx + (index % 2 == 0 ? 0 : thick - tick), cy, tick, thick);
            Place(corners[index * 2 + 1], cx, cy + (index < 2 ? 0 : thick - tick), thick, tick);
        }

        if (visible && badge.Visibility != Visibility.Visible) ProductMotion.Reveal(badge);
        badge.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        dimensions.Text = hovered?.Title is { } name ? $"{name}   {rect.Width} × {rect.Height}" : $"{rect.Width} × {rect.Height}";
        dimensions.TextTrimming = TextTrimming.CharacterEllipsis;
        Canvas.SetLeft(badge, Math.Clamp(x + 2, 4, Math.Max(4, w - 190)));
        Canvas.SetTop(badge, y + height + 36 <= h ? y + height + 4 : Math.Max(0, y - 34));
    }

    private static void Place(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private void Finish(Selection? selection)
    {
        ShowWindow(Handle(this), Hide);
        result.TrySetResult(selection);
        Close();
    }

    private sealed class SelectionSurface : Grid
    {
        public SelectionSurface(bool crosshair)
        {
            if (crosshair) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        }
    }
}
