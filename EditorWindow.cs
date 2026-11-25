using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private readonly Button undoButton;
    private readonly Button redoButton;
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
        var root = new Grid { Background = Appearance.Surface };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(53) });
        var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Padding = new Thickness(10, 5, 10, 5) };
        var toolNames = new[] { "Arrow", "Rectangle", "Ellipse", "Pen", "Highlight", "Redact", "Text", "Step marker", "Crop" };
        var toolIcons = new[] { Glyph.Arrow, Glyph.Box, Glyph.Oval, Glyph.Pen, Glyph.Highlight, Glyph.Redact, Glyph.Text, Glyph.Step, Glyph.Crop };
        var toolButtons = new Button[toolNames.Length];
        for (var index = 0; index < toolNames.Length; index++)
        {
            var button = Appearance.IconButton(toolIcons[index], toolNames[index], size: 35);
            toolButtons[index] = button;
            icons.Children.Add(button);
            if (index is 2 or 5 or 7) icons.Children.Add(Appearance.Divider());
        }
        icons.Children.Add(Appearance.Divider());
        undoButton = Appearance.IconButton(Glyph.Undo, "Undo", Undo, 35);
        redoButton = Appearance.IconButton(Glyph.Redo, "Redo", Redo, 35);
        icons.Children.Add(undoButton);
        icons.Children.Add(redoButton);
        var bar = new Grid { Background = Appearance.Surface, Padding = new Thickness(0, 0, 10, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(new ScrollViewer
        {
            Content = icons, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled
        });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
        var viewport = new Grid { Background = Appearance.Canvas };
        canvas.ClearColor = Microsoft.UI.Colors.Transparent;
        canvas.Draw += OnDraw;
        canvas.PointerPressed += OnPress;
        canvas.PointerMoved += OnMove;
        canvas.PointerReleased += OnRelease;
        canvas.PointerCaptureLost += (_, _) => { points = null; canvas.Invalidate(); };
        viewport.Children.Add(canvas);
        body.Children.Add(viewport);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var toolPanel = Inspector();
        var toolName = Appearance.Label("", 20, strong: true);
        toolPanel.Children.Add(toolName);
        var swatchBrush = new SolidColorBrush(ink.Color);
        var colorButton = ColorButton("Stroke color", ink, swatchBrush);
        toolPanel.Children.Add(colorButton);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        AddSwatch("Orange", 255, 133, 87);
        AddSwatch("Yellow", 255, 207, 84);
        AddSwatch("Blue", 84, 155, 255);
        AddSwatch("White", 255, 255, 255);
        AddSwatch("Black", 19, 19, 22);
        toolPanel.Children.Add(colors);
        var widthDisplay = Appearance.Label("Stroke width · 5 px", 12, Appearance.Muted, strong: true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(weight, "Stroke width");
        weight.HorizontalAlignment = HorizontalAlignment.Stretch;
        weight.Width = double.NaN;
        weight.ValueChanged += (_, e) => widthDisplay.Text = $"Stroke width · {e.NewValue:0} px";
        toolPanel.Children.Add(widthDisplay);
        toolPanel.Children.Add(weight);
        text.Width = double.NaN;
        text.TextWrapping = TextWrapping.Wrap;
        text.MinHeight = 42;
        toolPanel.Children.Add(text);

        var paperPanel = Inspector();
        paperPanel.Children.Add(Appearance.Label("Background", 20, strong: true));
        background.Header = null;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(background, "Enable background");
        paperPanel.Children.Add(background);
        paperPanel.Children.Add(ColorButton("Background color", paper, new SolidColorBrush(paper.Color)));
        paperPanel.Children.Add(padding);
        paperPanel.Children.Add(rounding);
        aspect.HorizontalAlignment = HorizontalAlignment.Stretch;
        paperPanel.Children.Add(aspect);
        var inspector = new ScrollViewer
        {
            Content = toolPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        var inspectorFrame = new Border
        {
            Child = inspector, Background = Appearance.Surface, BorderBrush = Appearance.Line,
            BorderThickness = new Thickness(1, 0, 0, 0)
        };
        Grid.SetColumn(inspectorFrame, 1);
        body.Children.Add(inspectorFrame);

        var backgroundButton = Appearance.Action("Background", icon: Glyph.Background);
        backgroundButton.VerticalAlignment = VerticalAlignment.Center;
        backgroundButton.Click += (_, _) =>
        {
            var showPaper = ReferenceEquals(inspector.Content, toolPanel);
            inspector.Content = showPaper ? paperPanel : toolPanel;
            Appearance.SetTone(backgroundButton, showPaper ? ButtonTone.Selected : ButtonTone.Quiet);
            inspector.ChangeView(null, 0, null, disableAnimation: true);
            ProductMotion.Reveal((FrameworkElement)inspector.Content);
        };
        Grid.SetColumn(backgroundButton, 1);
        bar.Children.Add(backgroundButton);
        root.Children.Add(new Border
        {
            Child = bar, BorderBrush = Appearance.Line, BorderThickness = new Thickness(0, 1, 0, 1)
        });

        background.Toggled += (_, _) => { SetStatus(); canvas.Invalidate(); };
        paper.ColorChanged += (_, _) => canvas.Invalidate();
        padding.ValueChanged += (_, _) => { SetStatus(); canvas.Invalidate(); };
        rounding.ValueChanged += (_, _) => canvas.Invalidate();
        aspect.SelectionChanged += (_, _) => { SetStatus(); canvas.Invalidate(); };
        for (var index = 0; index < toolButtons.Length; index++)
        {
            var next = (Tool)index;
            toolButtons[index].Click += (_, _) => SelectTool(next);
        }
        SelectTool(Tool.Arrow);
        root.ActualThemeChanged += (_, _) => canvas.Invalidate();

        var footer = new Grid { Padding = new Thickness(13, 0, 13, 0), ColumnSpacing = 7 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(status);
        AddAction("Copy image", Glyph.Copy, CopyAsync, 1, ButtonTone.Raised);
        AddAction("Export PNG", Glyph.Save, () => save(RenderFrame()), 2, ButtonTone.Primary);
        var footerFrame = new Border
        {
            Child = footer, BorderBrush = Appearance.Line, BorderThickness = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(footerFrame, 2);
        root.Children.Add(footerFrame);
        Appearance.SetContent(this, root, caption: "Annotate");
        Closed += (_, _) => { closed = true; bitmap?.Dispose(); canvas.RemoveFromVisualTree(); };
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 1040, 678, .5, .45);
        status.Text = "Opening screenshot…";

        void SelectTool(Tool current)
        {
            tool = current;
            toolName.Text = toolNames[(int)tool];
            for (var index = 0; index < toolButtons.Length; index++)
            {
                var selected = index == (int)tool;
                Appearance.SetTone(toolButtons[index], selected ? ButtonTone.Selected : ButtonTone.Quiet);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(toolButtons[index], selected ? "Selected" : "");
            }
            text.Visibility = tool == Tool.Text ? Visibility.Visible : Visibility.Collapsed;
            if (ReferenceEquals(inspector.Content, paperPanel)) inspector.Content = toolPanel;
            Appearance.SetTone(backgroundButton, ButtonTone.Quiet);
            SetStatus();
        }

        void AddSwatch(string name, byte r, byte g, byte b)
        {
            var color = Color.FromArgb(255, r, g, b);
            var button = Appearance.Action(name, () => ink.Color = color);
            button.Width = 26;
            button.Height = 26;
            button.Padding = new Thickness(0);
            button.Background = new SolidColorBrush(color);
            button.BorderBrush = Appearance.Line;
            button.BorderThickness = new Thickness(1);
            button.Content = null;
            ToolTipService.SetToolTip(button, name);
            colors.Children.Add(button);
        }

        void AddAction(string name, Glyph glyph, Func<Task> command, int column, ButtonTone tone)
        {
            var button = Appearance.Action(name, icon: glyph, tone: tone);
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

    private static StackPanel Inspector() => new() { Padding = new Thickness(14, 14, 14, 12), Spacing = 11 };

    private static Button ColorButton(string label, ColorPicker picker, SolidColorBrush swatch)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(4), Background = swatch });
        row.Children.Add(Appearance.Label(label, 12, strong: true));
        var button = Appearance.Action(label, tone: ButtonTone.Raised);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Content = row;
        button.Flyout = new Flyout { Content = picker };
        picker.ColorChanged += (_, e) => swatch.Color = e.NewColor;
        return button;
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
        status.Text = $"{(int)size.Width} × {(int)size.Height} px   ·   {marks.Count} {(marks.Count == 1 ? "edit" : "edits")}";
        undoButton.IsEnabled = marks.Count != 0;
        redoButton.IsEnabled = redo.Count != 0;
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
