using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Numerics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;

namespace RealCaptureX;

internal sealed class EditorWindow : Window
{
    private enum Tool { Arrow, Box, Oval, Pen, Highlight, Cover, Text, Step, Crop }
    private sealed record Mark(Tool Tool, Vector2[] Points, Color Color, int Weight, string Text);
    private readonly CaptureEntry entry;
    private readonly Func<CaptureFrame, Task> save;
    private readonly CanvasControl canvas = new();
    private readonly List<Mark> marks = [];
    private readonly Stack<Mark> redo = new();
    private readonly TextBlock status = Appearance.Label("", 12, Appearance.Muted);
    private readonly TextBox text = new() { PlaceholderText = "Enter label, then click the image", MaxLength = 256, Width = 235 };
    private readonly ColorPicker ink = new() { Color = Color.FromArgb(255, 255, 133, 87), IsAlphaEnabled = false };
    private readonly ColorPicker paper = new() { Color = Color.FromArgb(255, 60, 75, 113), IsAlphaEnabled = false };
    private readonly Slider weight = new() { Minimum = 2, Maximum = 24, Value = 5, Width = 92, StepFrequency = 1 };
    private readonly Slider padding = new() { Header = "Padding", Maximum = 280, Value = 64, StepFrequency = 1 };
    private readonly Slider rounding = new() { Header = "Corner radius", Maximum = 64, Value = 12, StepFrequency = 1 };
    private readonly ComboBox aspect = new()
    {
        Header = "Canvas ratio", ItemsSource = new[] { "Original", "16:9", "4:3", "1:1", "4:5", "9:16" }, SelectedIndex = 0
    };
    private readonly ToggleSwitch background = new() { Header = "Background", OffContent = "Off", OnContent = "On" };
    private CanvasBitmap? bitmap;
    private Tool tool = Tool.Arrow;
    private List<Vector2>? points;
    private Vector2 origin;
    private float scale = 1;
    private bool closed;

    public EditorWindow(CaptureEntry entry, Func<CaptureFrame, Task> save)
    {
        this.entry = entry;
        this.save = save;
        Title = "REAL Capture X — Annotate";
        Appearance.StyleTitleBar(this);
        var root = new Grid { Background = Appearance.Surface, RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Padding = new Thickness(10, 7, 10, 7) };
        var tools = new ComboBox { Width = 132, ItemsSource = new[] { "Arrow", "Rectangle", "Ellipse", "Pen", "Highlight", "Redact", "Text", "Step marker", "Crop" }, SelectedIndex = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tools, "Annotation tool");
        tools.SelectionChanged += (_, _) =>
        {
            tool = (Tool)Math.Max(0, tools.SelectedIndex);
            text.Visibility = tool == Tool.Text ? Visibility.Visible : Visibility.Collapsed;
            SetStatus();
        };
        bar.Children.Add(tools);
        bar.Children.Add(Popup("Color", ink));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(weight, "Stroke width");
        bar.Children.Add(weight);
        text.Visibility = Visibility.Collapsed;
        bar.Children.Add(text);
        bar.Children.Add(Appearance.Divider());
        bar.Children.Add(Appearance.IconButton("\uE7A7", "Undo", Undo));
        bar.Children.Add(Appearance.IconButton("\uE7A6", "Redo", Redo));
        var paperPanel = new StackPanel { Width = 210, Spacing = 9 };
        paperPanel.Children.Add(background);
        paperPanel.Children.Add(Popup("Background color", paper));
        paperPanel.Children.Add(padding);
        paperPanel.Children.Add(rounding);
        paperPanel.Children.Add(aspect);
        bar.Children.Add(Popup("Background", paperPanel));
        background.Toggled += (_, _) => { SetStatus(); canvas.Invalidate(); };
        paper.ColorChanged += (_, _) => canvas.Invalidate();
        padding.ValueChanged += (_, _) => { SetStatus(); canvas.Invalidate(); };
        rounding.ValueChanged += (_, _) => canvas.Invalidate();
        aspect.SelectionChanged += (_, _) => { SetStatus(); canvas.Invalidate(); };
        var barScroll = new ScrollViewer
        {
            Content = bar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled
        };
        root.Children.Add(barScroll);

        canvas.ClearColor = Color.FromArgb(255, 16, 18, 22);
        canvas.Draw += OnDraw;
        canvas.PointerPressed += OnPress;
        canvas.PointerMoved += OnMove;
        canvas.PointerReleased += OnRelease;
        canvas.PointerCaptureLost += (_, _) => { points = null; canvas.Invalidate(); };
        Grid.SetRow(canvas, 1);
        root.Children.Add(canvas);

        var footer = new Grid { Padding = new Thickness(14, 8, 14, 8), ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(status);
        AddAction("Copy image", CopyAsync, 1);
        AddAction("Save edited PNG", () => save(RenderFrame()), 2);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        Closed += (_, _) => { closed = true; bitmap?.Dispose(); canvas.RemoveFromVisualTree(); };
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 1030, 670, 0.5, 0.45);
        status.Text = "Opening screenshot…";

        void AddAction(string name, Func<Task> command, int column)
        {
            var button = new Button { Content = name };
            button.Click += async (_, _) =>
            {
                if (bitmap is null) return;
                button.IsEnabled = false;
                try { await command(); }
                catch (Exception ex) { status.Text = ex.Message; }
                finally { button.IsEnabled = true; }
            };
            Grid.SetColumn(button, column);
            footer.Children.Add(button);
        }
    }

    public async Task LoadAsync()
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.Path);
        using var stream = await file.OpenReadAsync();
        var loaded = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), stream, 96);
        if (closed) { loaded.Dispose(); return; }
        bitmap = loaded;
        SetStatus();
        canvas.Invalidate();
    }

    private static Button Popup(string label, FrameworkElement content) =>
        new() { Content = label, Flyout = new Flyout { Content = content } };

    private void OnPress(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(canvas);
        if (bitmap is null || !pointer.Properties.IsLeftButtonPressed) return;
        var p = FromCanvas(pointer.Position);
        if (!ImageBounds.Contains(new Point(p.X, p.Y))) return;
        if (tool == Tool.Text && string.IsNullOrWhiteSpace(text.Text))
        {
            text.Focus(FocusState.Programmatic);
            return;
        }
        if (tool is Tool.Text or Tool.Step)
        {
            Commit([p]);
            return;
        }
        if (canvas.CapturePointer(e.Pointer)) points = [p, p];
    }

    private void OnMove(object sender, PointerRoutedEventArgs e)
    {
        if (points is null) return;
        var p = FromCanvas(e.GetCurrentPoint(canvas).Position);
        p = Vector2.Clamp(p, Vector2.Zero, new Vector2(entry.Width, entry.Height));
        if (tool == Tool.Pen) points.Add(p);
        else points[^1] = p;
        canvas.Invalidate();
    }

    private void OnRelease(object sender, PointerRoutedEventArgs e)
    {
        if (points is null) return;
        OnMove(sender, e);
        var finished = points.ToArray();
        points = null;
        canvas.ReleasePointerCapture(e.Pointer);
        if (Vector2.Distance(finished[0], finished[^1]) < 2 && tool == Tool.Crop) return;
        Commit(finished);
    }

    private void Commit(Vector2[] positions)
    {
        var label = tool == Tool.Step ? (marks.Count(mark => mark.Tool == Tool.Step) + 1).ToString() : text.Text.Trim();
        marks.Add(new Mark(tool, positions, ink.Color, (int)weight.Value, label));
        redo.Clear();
        SetStatus();
        canvas.Invalidate();
    }

    private void Undo()
    {
        if (marks.Count == 0) return;
        redo.Push(marks[^1]);
        marks.RemoveAt(marks.Count - 1);
        SetStatus();
        canvas.Invalidate();
    }

    private void Redo()
    {
        if (!redo.TryPop(out var mark)) return;
        marks.Add(mark);
        SetStatus();
        canvas.Invalidate();
    }

    private Rect ImageBounds => new(0, 0, entry.Width, entry.Height);
    private Rect Crop => marks.LastOrDefault(mark => mark.Tool == Tool.Crop) is { } mark
        ? MarkBounds(mark.Points[0], mark.Points[^1]) : ImageBounds;
    private int Padding => background.IsOn ? (int)padding.Value : 0;
    private Vector2 FromCanvas(Point p) => (new Vector2((float)p.X, (float)p.Y) - origin) / scale;
    private static Rect MarkBounds(Vector2 a, Vector2 b) =>
        new(Math.Floor(Math.Min(a.X, b.X)), Math.Floor(Math.Min(a.Y, b.Y)),
            Math.Max(1, Math.Ceiling(Math.Abs(a.X - b.X))), Math.Max(1, Math.Ceiling(Math.Abs(a.Y - b.Y))));

    private Rect PaperBounds(Rect crop)
    {
        var width = crop.Width + 2 * Padding;
        var height = crop.Height + 2 * Padding;
        var ratio = background.IsOn ? aspect.SelectedIndex switch
        {
            1 => 16d / 9, 2 => 4d / 3, 3 => 1, 4 => 4d / 5, 5 => 9d / 16, _ => 0
        } : 0;
        if (ratio > 0)
        {
            if (width / height > ratio) height = Math.Ceiling(width / ratio);
            else width = Math.Ceiling(height * ratio);
        }
        return new Rect(0, 0, width, height);
    }

    private void SetStatus()
    {
        var size = PaperBounds(Crop);
        status.Text = $"{(int)size.Width} × {(int)size.Height}   ·   {marks.Count} edits";
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs e)
    {
        if (bitmap is null) return;
        var crop = Crop;
        var paperRect = PaperBounds(crop);
        var output = new Vector2((float)paperRect.Width, (float)paperRect.Height);
        scale = Math.Max(0.01f, Math.Min((float)(sender.ActualWidth - 36) / output.X,
            (float)(sender.ActualHeight - 36) / output.Y));
        var placement = (new Vector2((float)sender.ActualWidth, (float)sender.ActualHeight) - output * scale) / 2;
        origin = placement + new Vector2((float)((paperRect.Width - crop.Width) / 2 - crop.X),
            (float)((paperRect.Height - crop.Height) / 2 - crop.Y)) * scale;
        e.DrawingSession.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(placement);
        Draw(e.DrawingSession, crop, preview: true);
    }

    private void Draw(CanvasDrawingSession ds, Rect crop, bool preview)
    {
        var paperRect = PaperBounds(crop);
        var cutout = new Rect((paperRect.Width - crop.Width) / 2, (paperRect.Height - crop.Height) / 2, crop.Width, crop.Height);
        if (background.IsOn)
            ds.FillRectangle(paperRect, paper.Color);
        using var mask = CanvasGeometry.CreateRoundedRectangle(ds, cutout,
            background.IsOn ? (float)rounding.Value : 0, background.IsOn ? (float)rounding.Value : 0);
        var transform = ds.Transform;
        using (ds.CreateLayer(1, mask))
        {
            ds.Transform = Matrix3x2.CreateTranslation((float)(cutout.X - crop.X), (float)(cutout.Y - crop.Y)) * transform;
            ds.DrawImage(bitmap!, 0, 0);
            foreach (var mark in marks) DrawMark(ds, mark);
            if (preview && points is { Count: > 0 })
            {
                if (tool == Tool.Crop) ds.DrawRectangle(MarkBounds(points[0], points[^1]), ink.Color, 2 / scale);
                else DrawMark(ds, new Mark(tool, points.ToArray(), ink.Color, (int)weight.Value, text.Text));
            }
        }
        ds.Transform = transform;
    }

    private static void DrawMark(CanvasDrawingSession ds, Mark mark)
    {
        var a = mark.Points[0];
        var b = mark.Points[^1];
        var box = MarkBounds(a, b);
        switch (mark.Tool)
        {
            case Tool.Arrow:
                if (a == b) break;
                ds.DrawLine(a, b, mark.Color, mark.Weight);
                var axis = Vector2.Normalize(b - a);
                var side = new Vector2(-axis.Y, axis.X) * (mark.Weight * 2);
                var tail = b - axis * (mark.Weight * 4);
                ds.DrawLine(b, tail + side, mark.Color, mark.Weight);
                ds.DrawLine(b, tail - side, mark.Color, mark.Weight);
                break;
            case Tool.Box: ds.DrawRectangle(box, mark.Color, mark.Weight); break;
            case Tool.Oval:
                ds.DrawEllipse((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2),
                    (float)box.Width / 2, (float)box.Height / 2, mark.Color, mark.Weight);
                break;
            case Tool.Pen:
                ds.FillCircle(a, mark.Weight / 2f, mark.Color);
                for (var i = 1; i < mark.Points.Length; i++)
                {
                    ds.DrawLine(mark.Points[i - 1], mark.Points[i], mark.Color, mark.Weight);
                    ds.FillCircle(mark.Points[i], mark.Weight / 2f, mark.Color);
                }
                break;
            case Tool.Highlight:
                ds.FillRectangle(box, Color.FromArgb(100, mark.Color.R, mark.Color.G, mark.Color.B));
                break;
            case Tool.Cover: ds.FillRectangle(box, Microsoft.UI.Colors.Black); break;
            case Tool.Text:
                using (var format = new CanvasTextFormat { FontFamily = "Segoe UI", FontSize = mark.Weight * 5 })
                    ds.DrawText(mark.Text, a, mark.Color, format);
                break;
            case Tool.Step:
                var radius = mark.Weight + 9;
                ds.FillCircle(a, radius, mark.Color);
                using (var format = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI", FontSize = radius, HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center
                })
                    ds.DrawText(mark.Text, a, Microsoft.UI.Colors.Black, format);
                break;
        }
    }

    private CaptureFrame RenderFrame()
    {
        var crop = Crop;
        var size = PaperBounds(crop);
        var width = (int)size.Width;
        var height = (int)size.Height;
        var device = CanvasDevice.GetSharedDevice();
        if (width > device.MaximumBitmapSizeInPixels || height > device.MaximumBitmapSizeInPixels ||
            (long)width * height > 40_000_000)
            throw new InvalidOperationException("The output is too large. Crop the screenshot or choose another canvas ratio.");
        using var target = new CanvasRenderTarget(device, width, height, 96);
        using (var drawing = target.CreateDrawingSession())
        {
            drawing.Clear(Microsoft.UI.Colors.Transparent);
            Draw(drawing, crop, preview: false);
        }
        return new CaptureFrame(width, height, target.GetPixelBytes(), HasAlpha: !background.IsOn);
    }

    private async Task CopyAsync()
    {
        using var png = await RenderFrame().EncodeAsync();
        var data = new DataPackage();
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(png));
        Clipboard.SetContent(data);
        Clipboard.Flush();
        status.Text = "Copied edited image";
    }
}
