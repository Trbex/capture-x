using Microsoft.UI.Xaml;
using Microsoft.UI.Input;
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
    private readonly Canvas canvas;
    private readonly Rectangle[] shade = new Rectangle[4];
    private readonly Rectangle outline = new() { Stroke = Appearance.Accent, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Border badge;
    private readonly TextBlock dimensions = Appearance.Label("", 13);
    private readonly Image image = new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill, IsHitTestVisible = false };
    private NativeWindow.Point? start;
    private RectInt32 area;
    private Target? hovered;

    public SelectionWindow(CaptureFrame frame, NativeWindow.Rect display, IReadOnlyList<Target>? windows)
    {
        this.frame = frame;
        this.display = display;
        this.windows = windows;
        canvas = new SelectionCanvas(windows is null)
        {
            IsTabStop = true, Background = Appearance.Brush(0, 0, 0, 0)
        };
        Title = windows is null ? "REAL Capture X — Select region" : "REAL Capture X — Select window";
        ToolWindow(this, rounded: false);

        var root = new Grid { Background = Appearance.Surface, RequestedTheme = ElementTheme.Dark };
        root.Children.Add(image);
        root.Children.Add(canvas);
        for (var i = 0; i < shade.Length; i++)
        {
            shade[i] = new Rectangle { Fill = Appearance.Brush(12, 14, 18, 139), IsHitTestVisible = false };
            canvas.Children.Add(shade[i]);
        }
        canvas.Children.Add(outline);
        badge = MakeBadge(dimensions);
        canvas.Children.Add(badge);
        var instruction = MakeBadge(Appearance.Label(windows is null
            ? "Drag to select  ·  Shift for square  ·  Esc to cancel"
            : "Select a window  ·  Esc to cancel", 14));
        instruction.HorizontalAlignment = HorizontalAlignment.Center;
        instruction.VerticalAlignment = VerticalAlignment.Top;
        instruction.Margin = new Thickness(0, 23, 0, 0);
        instruction.IsHitTestVisible = false;
        root.Children.Add(instruction);
        Content = root;

        canvas.PointerMoved += OnPointerMoved;
        canvas.PointerPressed += OnPointerPressed;
        canvas.PointerReleased += OnPointerReleased;
        canvas.PointerCaptureLost += (_, _) =>
        {
            if (start is null) return;
            start = null;
            area = default;
            UpdateMask(default);
        };
        canvas.SizeChanged += (_, _) => UpdateMask(area);
        canvas.Loaded += (_, _) => canvas.Focus(FocusState.Programmatic);
        canvas.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Finish(null);
            }
        };
        Closed += (_, _) => result.TrySetResult(null);
    }

    private static Border MakeBadge(TextBlock text) => new()
    {
        Child = text, CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 6, 11, 6),
        Background = Appearance.Surface, BorderBrush = Appearance.Line, BorderThickness = new Thickness(1),
        IsHitTestVisible = false, MaxWidth = 500
    };

    public Task<Selection?> PickAsync()
    {
        var source = new WriteableBitmap(frame.Width, frame.Height);
        using (var stream = source.PixelBuffer.AsStream())
            stream.Write(frame.Bgra);
        source.Invalidate();
        image.Source = source;
        var hwnd = Handle(this);
        AppWindow.MoveAndResize(new RectInt32(display.Left, display.Top, display.Width, display.Height));
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        Activate();
        SetWindowPos(hwnd, Topmost, 0, 0, 0, 0, NoActivate | 0x0003);
        SetForegroundWindow(hwnd);
        canvas.Focus(FocusState.Programmatic);
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
        if (start is not { } origin) return;
        var a = origin;
        var b = ToPixel(position, e.Pointer.PointerDeviceType);
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
        canvas.CapturePointer(e.Pointer);
        start = ToPixel(pointer.Position, e.Pointer.PointerDeviceType);
        area = default;
        UpdateMask(area);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (start is null) return;
        OnPointerMoved(sender, e);
        start = null;
        canvas.ReleasePointerCapture(e.Pointer);
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
        UpdateMask(area);
    }

    private NativeWindow.Point ToPixel(Point point, PointerDeviceType type)
    {
        if (type == PointerDeviceType.Mouse && GetCursorPos(out var cursor))
            return new NativeWindow.Point
            {
                X = Math.Clamp((int)Math.Round((cursor.X - display.Left) * (double)frame.Width / display.Width), 0, frame.Width),
                Y = Math.Clamp((int)Math.Round((cursor.Y - display.Top) * (double)frame.Height / display.Height), 0, frame.Height)
            };
        return new NativeWindow.Point
        {
            X = Math.Clamp((int)Math.Round(point.X * frame.Width / Math.Max(1, canvas.ActualWidth)), 0, frame.Width),
            Y = Math.Clamp((int)Math.Round(point.Y * frame.Height / Math.Max(1, canvas.ActualHeight)), 0, frame.Height)
        };
    }

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
        Place(outline, x, y, width, height);
        badge.Visibility = width > 0 && height > 0 ? Visibility.Visible : Visibility.Collapsed;
        dimensions.Text = hovered?.Title ?? $"{rect.Width} × {rect.Height}";
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

    private sealed class SelectionCanvas : Canvas
    {
        public SelectionCanvas(bool crosshair)
        {
            if (crosshair) ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        }
    }
}
