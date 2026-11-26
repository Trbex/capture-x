using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace RealCaptureX;

/// <summary>
/// Annotation editor. Marks are immutable records in a list; every change goes through a snapshot
/// checkpoint so undo/redo covers drawing, moving, resizing, restyling and deleting. One Win2D renderer
/// serves the live canvas, the clipboard copy and the PNG export.
/// </summary>
internal sealed class EditorWindow : Window
{
    private enum Tool { Select, Box, Oval, Arrow, Pen, Highlight, Text, Step, Blur, Crop }
    private enum Drag { None, Draw, Move, Handle, Pan }

    private sealed record TextStyle(string Font, float Size, bool Bold, bool Italic, bool Underline, Color Background, float Stroke);
    /// <summary>Head: 0 open, 1 filled, 2 dot. Dash: 0 solid, 1 dashed, 2 dotted. Mode (blur): 0 pixelate, 1 blur, 2 redact.</summary>
    private sealed record MarkStyle(Color Color, float Width, float Opacity = 1, bool Shadow = true, int Dash = 0, int Head = 1,
        bool Fill = false, int Mode = 0);
    private sealed record Mark(Tool Tool, Vector2[] Points, MarkStyle Style, string Text = "", TextStyle? Typography = null);

    private static readonly (Tool Tool, string Name, Glyph Icon, string Key)[] Tools =
    [
        (Tool.Select, "Select", Glyph.Select, "V"), (Tool.Box, "Rectangle", Glyph.Box, "R"), (Tool.Oval, "Ellipse", Glyph.Oval, "O"),
        (Tool.Arrow, "Arrow", Glyph.Arrow, "A"), (Tool.Pen, "Pen", Glyph.Pen, "P"), (Tool.Highlight, "Highlighter", Glyph.Highlight, "H"),
        (Tool.Text, "Text", Glyph.Text, "T"), (Tool.Step, "Counter", Glyph.Step, "N"), (Tool.Blur, "Blur", Glyph.Pixelate, "B"),
        (Tool.Crop, "Crop", Glyph.Crop, "C")
    ];

    private static readonly (string Name, Color Color)[] Palette =
    [
        ("Violet", Color.FromArgb(255, 139, 92, 246)), ("Red", Color.FromArgb(255, 255, 69, 58)),
        ("Yellow", Color.FromArgb(255, 255, 204, 0)), ("Blue", Color.FromArgb(255, 10, 132, 255)),
        ("White", Color.FromArgb(255, 255, 255, 255)), ("Black", Color.FromArgb(255, 18, 18, 20))
    ];

    private static readonly (string Name, Color Color)[] Papers =
    [
        ("Graphite", Color.FromArgb(255, 38, 40, 46)), ("Slate", Color.FromArgb(255, 84, 96, 122)),
        ("Mist", Color.FromArgb(255, 214, 219, 228)), ("Violet", Color.FromArgb(255, 120, 98, 214)),
        ("Ocean", Color.FromArgb(255, 44, 122, 196)), ("White", Color.FromArgb(255, 250, 250, 251))
    ];

    private readonly CaptureEntry entry;
    private readonly Func<CaptureFrame, Task> save;
    private readonly CanvasControl canvas = new();
    private readonly Canvas overlay = new();
    private readonly List<Mark> marks = [];
    private readonly Stack<List<Mark>> undo = new();
    private readonly Stack<List<Mark>> redo = new();
    private readonly Dictionary<Tool, MarkStyle> styles = new()
    {
        [Tool.Box] = new(Palette[1].Color, 4),
        [Tool.Oval] = new(Palette[1].Color, 4),
        [Tool.Arrow] = new(Palette[0].Color, 5),
        [Tool.Pen] = new(Palette[1].Color, 4),
        [Tool.Highlight] = new(Palette[2].Color, 0, .42f, Shadow: false),
        [Tool.Text] = new(Colors.White, 0),
        [Tool.Step] = new(Palette[0].Color, 18),
        [Tool.Blur] = new(Colors.Black, 12, Shadow: false),
        [Tool.Crop] = new(Colors.White, 2, Shadow: false)
    };
    private TextStyle typography = new("Segoe UI", 28, true, false, false, Colors.Transparent, 0);
    private readonly TextBlock dimensions = Appearance.Label("", 12, Appearance.Muted);
    private readonly TextBlock edits = Appearance.Label("", 12, Appearance.Muted);
    private readonly TextBlock zoomLabel = Appearance.Label("100%", 12, Appearance.Text, strong: true);
    private readonly Button undoButton;
    private readonly Button redoButton;
    private readonly Dictionary<Tool, Button> toolButtons = [];
    private readonly Border toolIndicator;
    private readonly Grid toolRow;
    private readonly Grid inspector;
    private readonly Grid toolbar;
    private readonly ScrollViewer inspectorBody = new() { HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock inspectorTitle = Appearance.Label("", 16, strong: true);
    private readonly Button backgroundButton;
    private readonly TextBox textEditor;
    private CanvasBitmap? bitmap;
    private Tool tool = Tool.Arrow;
    private int selection = -1;
    private bool showBackground;
    private bool inspectorOpen = true;
    private bool closed;

    // Background customization.
    private bool paperOn;
    private Color paper = Papers[1].Color;
    private double paperPadding = 64;
    private double paperRadius = 12;
    private int paperRatio;
    private bool paperShadow = true;

    // Pointer state.
    private Drag drag;
    private List<Vector2>? points;
    private Vector2 dragStart;
    private Vector2 panStart;
    private int handle;
    private Vector2 handleAnchor;
    private Mark? dragOriginal;
    private bool spaceDown;
    private Vector2? editingAt;
    private int editingIndex = -1;

    // View state: zoom 0 means "fit". View is the top-left of the paper in canvas DIPs when zoomed.
    private float zoom;
    private Vector2 view;
    private float scale = 1;
    private Vector2 origin;
    private Vector2 placement;
    private string? lastCheckpointKey;
    private DateTime lastCheckpoint;

    public EditorWindow(CaptureEntry entry, Func<CaptureFrame, Task> save)
    {
        this.entry = entry;
        this.save = save;
        Title = "REAL Capture X — Annotate";

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        var stage = new Grid { Background = Appearance.Canvas, Margin = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(12) };
        canvas.ClearColor = Microsoft.UI.Colors.Transparent;
        canvas.IsTabStop = true;
        canvas.UseSystemFocusVisuals = false;
        AutomationProperties.SetName(canvas, "Screenshot canvas");
        canvas.Draw += OnDraw;
        canvas.PointerPressed += OnPress;
        canvas.PointerMoved += OnMove;
        canvas.PointerReleased += OnRelease;
        canvas.PointerWheelChanged += OnWheel;
        canvas.DoubleTapped += OnDoubleTapped;
        canvas.PointerCaptureLost += (_, _) => { if (drag != Drag.None) EndDrag(); };
        canvas.SizeChanged += (_, _) => { PositionToolbar(); canvas.Invalidate(); };
        stage.Children.Add(canvas);
        stage.Children.Add(overlay);

        textEditor = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinWidth = 60, Padding = new Thickness(2, 0, 2, 0),
            Background = Appearance.Clear, BorderThickness = new Thickness(1), BorderBrush = Appearance.Brush(255, 255, 255, 120),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(textEditor, "Annotation text");
        textEditor.Resources["TextControlBackgroundFocused"] = Appearance.Clear;
        textEditor.Resources["TextControlBackgroundPointerOver"] = Appearance.Clear;
        textEditor.Resources["TextControlBorderBrushFocused"] = Appearance.Brush(255, 255, 255, 140);
        textEditor.PreviewKeyDown += (_, e) =>
        {
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (e.Key == VirtualKey.Enter && !shift) { e.Handled = true; FinishText(commit: true); }
            else if (e.Key == VirtualKey.Escape) { e.Handled = true; FinishText(commit: false); }
        };
        textEditor.LostFocus += (_, _) =>
        {
            if (FocusManager.GetFocusedElement(Content.XamlRoot) is DependencyObject next && inspector is not null && IsInside(next, inspector)) return;
            FinishText(commit: true);
        };
        textEditor.TextChanged += (_, _) => canvas.Invalidate();
        overlay.Children.Add(textEditor);

        // Floating tool bar centred over the canvas.
        toolRow = new Grid { Padding = new Thickness(6), ColumnSpacing = 0 };
        toolIndicator = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = Appearance.SelectedFill,
            BorderBrush = Appearance.SelectedRim, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        toolRow.Children.Add(toolIndicator);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var (kind, name, icon, key) in Tools)
        {
            if (kind is Tool.Pen or Tool.Text or Tool.Crop) buttons.Children.Add(BarDivider());
            var button = ToolButton(icon, $"{name}  ·  {key}");
            AutomationProperties.SetName(button, name);
            AutomationProperties.SetAcceleratorKey(button, key);
            button.Click += (_, _) => SelectTool(kind);
            toolButtons[kind] = button;
            buttons.Children.Add(button);
        }
        buttons.Children.Add(BarDivider());
        undoButton = ToolButton(Glyph.Undo, "Undo  ·  Ctrl+Z");
        redoButton = ToolButton(Glyph.Redo, "Redo  ·  Ctrl+Shift+Z");
        undoButton.Click += (_, _) => Undo();
        redoButton.Click += (_, _) => Redo();
        buttons.Children.Add(undoButton);
        buttons.Children.Add(redoButton);
        buttons.Children.Add(BarDivider());
        backgroundButton = ToolButton(Glyph.Background, "Background");
        backgroundButton.Click += (_, _) => ShowBackground(!showBackground || !inspectorOpen);
        buttons.Children.Add(backgroundButton);
        toolRow.Children.Add(buttons);
        toolbar = Appearance.Card(toolRow, 14);
        toolbar.HorizontalAlignment = HorizontalAlignment.Center;
        toolbar.VerticalAlignment = VerticalAlignment.Top;
        toolbar.Margin = new Thickness(0, 14, 0, 0);
        stage.Children.Add(toolbar);

        // Contextual properties panel, floating on the right.
        var head = new Grid { Padding = new Thickness(18, 14, 10, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(inspectorTitle);
        var hide = Appearance.IconButton(Glyph.Close, "Hide properties", () => SetInspector(false), 30);
        Grid.SetColumn(hide, 1);
        head.Children.Add(hide);
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        panel.Children.Add(head);
        Grid.SetRow(inspectorBody, 1);
        panel.Children.Add(inspectorBody);
        inspector = Appearance.Card(panel, 16);
        inspector.Width = 296;
        inspector.HorizontalAlignment = HorizontalAlignment.Right;
        inspector.Margin = new Thickness(0, 14, 14, 14);
        stage.Children.Add(inspector);
        root.Children.Add(stage);

        // Footer: size and edit count, zoom, primary actions.
        var footer = new Grid { Padding = new Thickness(20, 0, 14, 0), ColumnSpacing = 10 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        var info = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Symbols.Draw(Glyph.Crop, 16, Appearance.Muted));
        info.Children.Add(dimensions);
        info.Children.Add(new Border { Width = 1, Height = 14, Background = Appearance.Line });
        info.Children.Add(edits);
        footer.Children.Add(info);
        var zoomGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var zoomBox = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        zoomBox.Children.Add(Appearance.IconButton(Glyph.Minus, "Zoom out  ·  Ctrl+−", () => ZoomBy(1 / 1.25f), 32));
        zoomLabel.MinWidth = 52;
        zoomLabel.TextAlignment = TextAlignment.Center;
        zoomBox.Children.Add(zoomLabel);
        zoomBox.Children.Add(Appearance.IconButton(Glyph.Plus, "Zoom in  ·  Ctrl+=", () => ZoomBy(1.25f), 32));
        zoomGroup.Children.Add(new Border
        {
            Child = zoomBox, CornerRadius = new CornerRadius(10), Background = Appearance.Tint, BorderBrush = Appearance.Line,
            BorderThickness = new Thickness(1), Padding = new Thickness(2)
        });
        zoomGroup.Children.Add(Appearance.IconButton(Glyph.Fit, "Fit to window  ·  Ctrl+0", () => AnimateZoom(0), 34));
        Grid.SetColumn(zoomGroup, 1);
        footer.Children.Add(zoomGroup);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Command("Copy image", Glyph.Copy, CopyAsync, ButtonTone.Raised));
        actions.Children.Add(Command("Export PNG", Glyph.Save, () => save(RenderFrame()), ButtonTone.Primary));
        Grid.SetColumn(actions, 2);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        root.PreviewKeyDown += OnKey;
        root.KeyUp += (_, e) => { if (e.Key == VirtualKey.Space) spaceDown = false; };
        root.ActualThemeChanged += (_, _) => canvas.Invalidate();
        Appearance.SetContent(this, root, caption: "Annotate");
        Closed += (_, _) => { closed = true; bitmap?.Dispose(); canvas.RemoveFromVisualTree(); };
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 1180, 760, .5, .45);
        dimensions.Text = "Opening screenshot…";
        SelectTool(Tool.Arrow);
        root.Loaded += (_, _) =>
        {
            MoveIndicator(animate: false);
            canvas.Focus(FocusState.Programmatic);
        };

        Button Command(string name, Glyph glyph, Func<Task> run, ButtonTone tone)
        {
            var button = Appearance.Action(name, icon: glyph, tone: tone);
            button.Height = 36;
            button.Padding = new Thickness(14, 0, 16, 0);
            button.CornerRadius = new CornerRadius(10);
            button.Click += async (_, _) =>
            {
                if (bitmap is null) return;
                button.IsEnabled = false;
                try { await run(); }
                catch (Exception ex) { ToastWindow.Show("Action failed", ex.Message, Glyph.Close); }
                finally { button.IsEnabled = true; }
            };
            return button;
        }
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, container)) return true;
        return false;
    }

    private static Border BarDivider() => new()
    {
        Width = 1, Height = 22, Background = Appearance.Line, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center
    };

    private static Button ToolButton(Glyph glyph, string tip)
    {
        var button = Appearance.IconButton(glyph, tip, size: 40);
        button.CornerRadius = new CornerRadius(10);
        button.Content = Symbols.Draw(glyph, 20);
        ToolTipService.SetToolTip(button, tip);
        return button;
    }

    // ---------------------------------------------------------------- tools and inspector

    private void SelectTool(Tool next)
    {
        if (editingAt is not null) FinishText(commit: true);
        if (drag == Drag.Draw) EndDrag();
        tool = next;
        showBackground = false;
        if (next != Tool.Select) selection = -1;
        foreach (var (kind, button) in toolButtons)
            AutomationProperties.SetItemStatus(button, kind == next ? "Selected" : "");
        Appearance.SetTone(backgroundButton, ButtonTone.Quiet);
        MoveIndicator(animate: true);
        canvas.ProtectedCursorShape(next == Tool.Select ? InputSystemCursorShape.Arrow : InputSystemCursorShape.Cross);
        if (!inspectorOpen && next != Tool.Select) SetInspector(true);
        else BuildInspector();
        canvas.Invalidate();
    }

    private void MoveIndicator(bool animate)
    {
        var target = toolButtons[tool];
        if (target.ActualWidth <= 0) return;
        var offset = target.TransformToVisual(toolRow).TransformPoint(new Point(0, 0));
        ProductMotion.MoveTo(toolIndicator, new Vector2((float)offset.X - (float)toolRow.Padding.Left, 0), animate);
    }

    private void ShowBackground(bool visible)
    {
        showBackground = visible;
        Appearance.SetTone(backgroundButton, visible ? ButtonTone.Selected : ButtonTone.Quiet);
        if (visible && !inspectorOpen) SetInspector(true);
        else BuildInspector();
    }

    private void SetInspector(bool open)
    {
        if (inspectorOpen == open) return;
        inspectorOpen = open;
        inspector.IsHitTestVisible = open;
        if (open)
        {
            BuildInspector();
            ProductMotion.Enter(inspector, fromScale: .985f, slide: 28);
        }
        else _ = ProductMotion.ExitAsync(inspector, toScale: .985f, slide: 28);
        PositionToolbar();
        if (zoom == 0) AnimateFit();
    }

    /// <summary>Keeps the floating tool bar centred over the visible canvas, clear of the properties panel.</summary>
    private void PositionToolbar()
    {
        var right = inspectorOpen && canvas.ActualWidth - inspector.Width - 28 > toolbar.ActualWidth + 40 ? inspector.Width + 14 : 0;
        toolbar.Margin = new Thickness(0, 14, right, 0);
    }

    private Mark? Selected => selection >= 0 && selection < marks.Count ? marks[selection] : null;
    private Tool InspectedTool => Selected?.Tool ?? tool;

    /// <summary>Rebuilds the contextual properties for the active tool (or the selected mark).</summary>
    private void BuildInspector()
    {
        var page = new StackPanel { Padding = new Thickness(18, 4, 18, 18), Spacing = 18 };
        if (showBackground) BackgroundProperties(page);
        else
        {
            var kind = InspectedTool;
            inspectorTitle.Text = kind == Tool.Select ? "Select" : Tools.First(t => t.Tool == kind).Name;
            switch (kind)
            {
                case Tool.Select: SelectProperties(page); break;
                case Tool.Text: TextProperties(page); break;
                case Tool.Blur: BlurProperties(page); break;
                case Tool.Crop: CropProperties(page); break;
                default: StrokeProperties(page, kind); break;
            }
        }
        if (ReferenceEquals(inspectorBody.Content, null) || inspectorBody.Content is FrameworkElement)
        {
            inspectorBody.Content = page;
            inspectorBody.ChangeView(null, 0, null, disableAnimation: true);
            ProductMotion.Reveal(page);
        }
    }

    private MarkStyle StyleOf(Tool kind) => Selected is { } mark && mark.Tool == kind ? mark.Style : styles[kind];

    /// <summary>Applies a style change to the tool default and, when one is selected, to that mark (undoable).</summary>
    private void Restyle(Tool kind, Func<MarkStyle, MarkStyle> change, string key)
    {
        styles[kind] = change(styles[kind]);
        if (Selected is { } mark && mark.Tool == kind)
        {
            Checkpoint(key);
            marks[selection] = mark with { Style = change(mark.Style) };
            UpdateStatus();
        }
        canvas.Invalidate();
    }

    private void StrokeProperties(StackPanel page, Tool kind)
    {
        var style = StyleOf(kind);
        page.Children.Add(Section("Color", ColorRow(style.Color, color => Restyle(kind, s => s with { Color = color }, "color"))));
        if (kind is Tool.Box or Tool.Oval or Tool.Arrow or Tool.Pen)
            page.Children.Add(Section("Thickness", SliderRow(1, 24, style.Width, "px",
                value => Restyle(kind, s => s with { Width = (float)value }, "width"))));
        if (kind == Tool.Step)
            page.Children.Add(Section("Size", SliderRow(8, 40, style.Width, "px", value => Restyle(kind, s => s with { Width = (float)value }, "width"))));
        if (kind == Tool.Arrow)
            page.Children.Add(Section("Arrowhead", Segmented(style.Head, value => Restyle(kind, s => s with { Head = value }, "head"),
                LineSample(0, 0), LineSample(0, 1), LineSample(0, 2))));
        if (kind is Tool.Box or Tool.Oval or Tool.Arrow or Tool.Pen)
            page.Children.Add(Section("Style", Segmented(style.Dash, value => Restyle(kind, s => s with { Dash = value }, "dash"),
                LineSample(0, -1), LineSample(1, -1), LineSample(2, -1))));
        if (kind is Tool.Box or Tool.Oval)
            page.Children.Add(Toggle("Fill", style.Fill, on => Restyle(kind, s => s with { Fill = on }, "fill")));
        page.Children.Add(Section("Opacity", SliderRow(10, 100, style.Opacity * 100, "%",
            value => Restyle(kind, s => s with { Opacity = (float)(value / 100) }, "opacity"))));
        if (kind != Tool.Highlight)
            page.Children.Add(Toggle("Shadow", style.Shadow, on => Restyle(kind, s => s with { Shadow = on }, "shadow")));
    }

    private void BlurProperties(StackPanel page)
    {
        var style = StyleOf(Tool.Blur);
        page.Children.Add(Section("Effect", Segmented(style.Mode, value =>
            {
                Restyle(Tool.Blur, s => s with { Mode = value }, "mode");
                BuildInspector();
            },
            Text("Pixelate"), Text("Blur"), Text("Redact"))));
        if (style.Mode != 2)
            page.Children.Add(Section("Strength", SliderRow(4, 40, style.Width, "",
                value => Restyle(Tool.Blur, s => s with { Width = (float)value }, "strength"))));
        page.Children.Add(Hint("Drag over sensitive content. The effect is baked into copies and exports."));

        static TextBlock Text(string value) => Appearance.Label(value, 12, strong: true);
    }

    private void CropProperties(StackPanel page)
    {
        page.Children.Add(Hint("Drag on the image to choose the area to keep. Crop is undoable."));
        var reset = Appearance.Action("Reset crop", () =>
        {
            if (!marks.Any(m => m.Tool == Tool.Crop)) return;
            Checkpoint(null);
            marks.RemoveAll(m => m.Tool == Tool.Crop);
            selection = -1;
            UpdateStatus();
            canvas.Invalidate();
        }, Glyph.Undo, ButtonTone.Raised);
        reset.HorizontalAlignment = HorizontalAlignment.Stretch;
        page.Children.Add(reset);
    }

    private void SelectProperties(StackPanel page)
    {
        if (Selected is not null)
        {
            var delete = Appearance.Action("Delete annotation", DeleteSelection, Glyph.Trash, ButtonTone.Raised);
            delete.HorizontalAlignment = HorizontalAlignment.Stretch;
            page.Children.Add(delete);
        }
        page.Children.Add(Hint("Click an annotation to select it. Drag to move, drag the handles to resize, Delete to remove. Double-click text to edit it."));
    }

    private void TextProperties(StackPanel page)
    {
        var style = StyleOf(Tool.Text);
        var type = Selected is { Tool: Tool.Text, Typography: { } selectedType } ? selectedType : typography;
        var font = new ComboBox
        {
            ItemsSource = new[] { "Segoe UI", "Segoe UI Variable Display", "Arial", "Georgia", "Consolas" },
            SelectedItem = type.Font, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(font, "Font");
        font.SelectionChanged += (_, _) => Retype(t => t with { Font = (string)font.SelectedItem }, "font");
        page.Children.Add(Section("Font", font));
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        toggles.Children.Add(Format("B", "Bold", type.Bold, on => Retype(t => t with { Bold = on }, "bold")));
        toggles.Children.Add(Format("I", "Italic", type.Italic, on => Retype(t => t with { Italic = on }, "italic")));
        toggles.Children.Add(Format("U", "Underline", type.Underline, on => Retype(t => t with { Underline = on }, "underline")));
        row.Children.Add(toggles);
        var size = new NumberBox { Minimum = 8, Maximum = 160, Value = type.Size, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        AutomationProperties.SetName(size, "Font size");
        size.ValueChanged += (_, e) => { if (double.IsFinite(e.NewValue)) Retype(t => t with { Size = (float)e.NewValue }, "size"); };
        Grid.SetColumn(size, 1);
        row.Children.Add(size);
        page.Children.Add(row);
        page.Children.Add(Section("Text color", ColorRow(style.Color, color => Restyle(Tool.Text, s => s with { Color = color }, "color"))));
        page.Children.Add(Section("Background", ColorRow(type.Background, color => Retype(t => t with { Background = color }, "background"),
            allowClear: true)));
        page.Children.Add(Toggle("Outline", type.Stroke > 0, on => Retype(t => t with { Stroke = on ? 2 : 0 }, "outline")));
        page.Children.Add(Toggle("Shadow", style.Shadow, on => Restyle(Tool.Text, s => s with { Shadow = on }, "shadow")));
        page.Children.Add(Hint("Click the image to type. Enter places the text, Shift+Enter adds a line."));

        Button Format(string letter, string name, bool on, Action<bool> set)
        {
            var button = Appearance.Action(name, tone: on ? ButtonTone.Selected : ButtonTone.Raised);
            button.Width = 36;
            button.Height = 34;
            button.Padding = new Thickness(0);
            var label = Appearance.Label(letter, 14, strong: name == "Bold");
            if (name == "Italic") label.FontStyle = Windows.UI.Text.FontStyle.Italic;
            if (name == "Underline") label.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            button.Content = label;
            ToolTipService.SetToolTip(button, name);
            var state = on;
            button.Click += (_, _) =>
            {
                state = !state;
                Appearance.SetTone(button, state ? ButtonTone.Selected : ButtonTone.Raised);
                set(state);
            };
            return button;
        }
    }

    private void Retype(Func<TextStyle, TextStyle> change, string key)
    {
        typography = change(typography);
        if (Selected is { Tool: Tool.Text, Typography: { } current } mark)
        {
            Checkpoint("text-" + key);
            marks[selection] = mark with { Typography = change(current) };
        }
        if (editingAt is not null) StyleEditor();
        canvas.Invalidate();
    }

    private void BackgroundProperties(StackPanel page)
    {
        inspectorTitle.Text = "Background";
        page.Children.Add(Toggle("Show background", paperOn, on => { paperOn = on; UpdateStatus(); AnimateFit(); }));
        var colors = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < Papers.Length; i++)
        {
            colors.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var (name, color) = Papers[i];
            var swatch = Swatch(name, color, paper == color);
            swatch.Click += (_, _) =>
            {
                paper = color;
                paperOn = true;
                BuildInspector();
                canvas.Invalidate();
            };
            Grid.SetColumn(swatch, i);
            colors.Children.Add(swatch);
        }
        page.Children.Add(Section("Color", colors));
        page.Children.Add(Section("Padding", SliderRow(0, 240, paperPadding, "px", value => { paperPadding = value; UpdateStatus(); canvas.Invalidate(); })));
        page.Children.Add(Section("Corner radius", SliderRow(0, 48, paperRadius, "px", value => { paperRadius = value; canvas.Invalidate(); })));
        page.Children.Add(Section("Aspect ratio", Segmented(paperRatio, value => { paperRatio = value; UpdateStatus(); AnimateFit(); },
            Ratio("Auto"), Ratio("16:9"), Ratio("4:3"), Ratio("1:1"))));
        page.Children.Add(Toggle("Screenshot shadow", paperShadow, on => { paperShadow = on; canvas.Invalidate(); }));

        static TextBlock Ratio(string value) => Appearance.Label(value, 12, strong: true);
    }

    // ---------------------------------------------------------------- inspector building blocks

    private static StackPanel Section(string title, FrameworkElement content)
    {
        var section = new StackPanel { Spacing = 9 };
        section.Children.Add(Appearance.Label(title, 13, Appearance.Text));
        section.Children.Add(content);
        if (string.IsNullOrEmpty(AutomationProperties.GetName(content))) AutomationProperties.SetName(content, title);
        return section;
    }

    private static TextBlock Hint(string text)
    {
        var hint = Appearance.Label(text, 12, Appearance.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        return hint;
    }

    private static Grid Toggle(string title, bool on, Action<bool> set)
    {
        var row = new Grid { MinHeight = 32 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Appearance.Label(title, 13));
        var toggle = new ToggleSwitch { IsOn = on, OnContent = null, OffContent = null, MinWidth = 0 };
        AutomationProperties.SetName(toggle, title);
        toggle.Toggled += (_, _) => set(toggle.IsOn);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    private static Grid SliderRow(double min, double max, double value, string unit, Action<double> changed)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, StepFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        var readout = Appearance.Label($"{value:0} {unit}".Trim(), 12, Appearance.Text);
        readout.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border
        {
            Child = readout, Height = 32, CornerRadius = new CornerRadius(8), Background = Appearance.Field,
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1)
        };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = $"{e.NewValue:0} {unit}".Trim();
            changed(e.NewValue);
        };
        row.Children.Add(slider);
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        return row;
    }

    private static Grid Segmented(int selected, Action<int> choose, params FrameworkElement[] options)
    {
        var row = new Grid { ColumnSpacing = 6 };
        var buttons = new List<Button>();
        for (var i = 0; i < options.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var index = i;
            var button = Appearance.Action($"Option {i + 1}", tone: i == selected ? ButtonTone.Selected : ButtonTone.Raised);
            button.Content = options[i];
            button.Height = 38;
            button.CornerRadius = new CornerRadius(9);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) =>
            {
                foreach (var other in buttons) Appearance.SetTone(other, ReferenceEquals(other, button) ? ButtonTone.Selected : ButtonTone.Raised);
                choose(index);
            };
            buttons.Add(button);
            Grid.SetColumn(button, i);
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>Tiny vector preview of a line style (dash) or arrowhead (head, -1 for none).</summary>
    private static FrameworkElement LineSample(int dash, int head)
    {
        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = Appearance.Text, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                head switch { 0 => "M2 7 H24 M19 2 L24 7 L19 12", 1 => "M2 7 H20", 2 => "M2 7 H20", _ => "M2 7 H28" })
        };
        if (dash == 1) path.StrokeDashArray = [3, 2.2];
        if (dash == 2) { path.StrokeDashArray = [0, 2.4]; path.StrokeDashCap = PenLineCap.Round; }
        var canvas = new Canvas { Width = 30, Height = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        canvas.Children.Add(path);
        if (head == 1)
            canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Fill = Appearance.Text, Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M18 1.5 L27 7 L18 12.5 Z")
            });
        if (head == 2)
            canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = Appearance.Text, Margin = new Thickness(19, 3, 0, 0) });
        return canvas;
    }

    private Grid ColorRow(Color current, Action<Color> choose, bool allowClear = false)
    {
        var row = new Grid { ColumnSpacing = 8 };
        var swatches = new List<(Button Button, Color Color)>();
        var options = allowClear ? Palette.Prepend(("None", Colors.Transparent)).ToArray() : Palette;
        for (var i = 0; i < options.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var (name, color) = options[i];
            var swatch = Swatch(name, color, color == current);
            swatch.Click += (_, _) =>
            {
                foreach (var (button, value) in swatches) Ring(button, value == color);
                choose(color);
            };
            swatches.Add((swatch, color));
            Grid.SetColumn(swatch, i);
            row.Children.Add(swatch);
        }
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var custom = Appearance.IconButton(Glyph.Plus, "Custom color", size: 30);
        custom.CornerRadius = new CornerRadius(15);
        custom.BorderBrush = Appearance.Line;
        custom.BorderThickness = new Thickness(1);
        var picker = new ColorPicker { Color = current.A == 0 ? Colors.White : current, IsAlphaEnabled = false };
        picker.ColorChanged += (_, e) =>
        {
            foreach (var (button, _) in swatches) Ring(button, false);
            choose(e.NewColor);
        };
        custom.Flyout = new Flyout { Content = picker };
        Grid.SetColumn(custom, options.Length);
        row.Children.Add(custom);
        return row;
    }

    private static Button Swatch(string name, Color color, bool selected)
    {
        var button = Appearance.Action(name);
        button.Width = button.Height = 30;
        button.Padding = new Thickness(3);
        button.CornerRadius = new CornerRadius(15);
        var dot = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(color),
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(color.A == 0 || color.R > 240 && color.G > 240 ? 1 : 0)
        };
        if (color.A == 0) dot.Child = Symbols.Draw(Glyph.ClearFill, 16, Appearance.Muted);
        button.Content = dot;
        ToolTipService.SetToolTip(button, name);
        Ring(button, selected);
        return button;
    }

    private static void Ring(Button swatch, bool selected)
    {
        swatch.BorderBrush = selected ? Appearance.Text : Appearance.Clear;
        swatch.BorderThickness = new Thickness(selected ? 2 : 0);
        AutomationProperties.SetItemStatus(swatch, selected ? "Selected" : "");
    }

    // ---------------------------------------------------------------- history

    /// <summary>Snapshots the mark list before a change. Rapid changes of the same property coalesce into one step.</summary>
    private void Checkpoint(string? key)
    {
        var now = DateTime.UtcNow;
        if (key is not null && key == lastCheckpointKey && now - lastCheckpoint < TimeSpan.FromMilliseconds(900))
        {
            lastCheckpoint = now;
            return;
        }
        lastCheckpointKey = key;
        lastCheckpoint = now;
        undo.Push([.. marks]);
        redo.Clear();
    }

    private void Undo()
    {
        if (!undo.TryPop(out var previous)) return;
        redo.Push([.. marks]);
        Restore(previous);
    }

    private void Redo()
    {
        if (!redo.TryPop(out var next)) return;
        undo.Push([.. marks]);
        Restore(next);
    }

    private void Restore(List<Mark> state)
    {
        marks.Clear();
        marks.AddRange(state);
        lastCheckpointKey = null;
        if (selection >= marks.Count) selection = -1;
        UpdateStatus();
        BuildInspector();
        canvas.Invalidate();
    }

    private void DeleteSelection()
    {
        if (Selected is null) return;
        Checkpoint(null);
        marks.RemoveAt(selection);
        selection = -1;
        UpdateStatus();
        BuildInspector();
        canvas.Invalidate();
    }

    // ---------------------------------------------------------------- loading and status

    public async Task LoadAsync()
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(entry.Path);
        using var stream = await file.OpenReadAsync();
        var loaded = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), stream, 96);
        if (closed) { loaded.Dispose(); return; }
        bitmap = loaded;
        UpdateStatus();
        canvas.Invalidate();
    }

    private void UpdateStatus()
    {
        var size = PaperBounds(Crop);
        dimensions.Text = $"{(int)size.Width} × {(int)size.Height} px";
        var count = marks.Count(m => m.Tool != Tool.Crop);
        edits.Text = $"{count} {(count == 1 ? "edit" : "edits")}";
        undoButton.IsEnabled = undo.Count != 0;
        redoButton.IsEnabled = redo.Count != 0;
    }

    // ---------------------------------------------------------------- geometry

    private Rect ImageBounds => new(0, 0, entry.Width, entry.Height);
    private Rect Crop => marks.LastOrDefault(mark => mark.Tool == Tool.Crop) is { } mark
        ? Intersect(Box(mark.Points[0], mark.Points[^1]), ImageBounds) : ImageBounds;
    private int Padding => paperOn ? (int)paperPadding : 0;
    private Vector2 FromCanvas(Point p) => (new Vector2((float)p.X, (float)p.Y) - origin) / scale;
    private Vector2 ToCanvas(Vector2 p) => p * scale + origin;

    private static Rect Box(Vector2 a, Vector2 b) =>
        new(Math.Floor(Math.Min(a.X, b.X)), Math.Floor(Math.Min(a.Y, b.Y)),
            Math.Max(1, Math.Ceiling(Math.Abs(a.X - b.X))), Math.Max(1, Math.Ceiling(Math.Abs(a.Y - b.Y))));

    private static Rect Intersect(Rect a, Rect b)
    {
        a.Intersect(b);
        return a.IsEmpty || a.Width < 1 || a.Height < 1 ? b : a;
    }

    private Rect PaperBounds(Rect crop)
    {
        var width = crop.Width + 2 * Padding;
        var height = crop.Height + 2 * Padding;
        var ratio = paperOn ? paperRatio switch { 1 => 16d / 9, 2 => 4d / 3, 3 => 1, _ => 0 } : 0;
        if (ratio > 0)
        {
            if (width / height > ratio) height = Math.Ceiling(width / ratio);
            else width = Math.Ceiling(height * ratio);
        }
        return new Rect(0, 0, width, height);
    }

    private float FitScale()
    {
        var output = PaperBounds(Crop);
        var available = Available();
        return Math.Max(0.02f, Math.Min(1.5f * (float)Math.Max(1, XamlRootScale()) / (float)XamlRootScale(),
            Math.Min((float)(available.Width / output.Width), (float)(available.Height / output.Height))));
    }

    private double XamlRootScale() => canvas.XamlRoot?.RasterizationScale ?? 1;

    /// <summary>Canvas area not covered by the floating tool bar and properties panel.</summary>
    private Rect Available()
    {
        var right = inspectorOpen ? inspector.Width + 28 : 0;
        return new Rect(28, 78, Math.Max(80, canvas.ActualWidth - 56 - right), Math.Max(80, canvas.ActualHeight - 78 - 28));
    }

    private Rect MarkBounds(Mark mark)
    {
        switch (mark.Tool)
        {
            case Tool.Text:
                using (var layout = TextLayout(CanvasDevice.GetSharedDevice(), mark))
                {
                    var bounds = layout.LayoutBounds;
                    return new Rect(mark.Points[0].X + bounds.X - 4, mark.Points[0].Y + bounds.Y - 4, bounds.Width + 8, bounds.Height + 8);
                }
            case Tool.Step:
                var r = mark.Style.Width;
                return new Rect(mark.Points[0].X - r, mark.Points[0].Y - r, r * 2, r * 2);
            case Tool.Pen:
                var min = mark.Points.Aggregate(Vector2.Min);
                var max = mark.Points.Aggregate(Vector2.Max);
                return Box(min, max);
            default:
                return Box(mark.Points[0], mark.Points[^1]);
        }
    }

    private int HitTest(Vector2 p)
    {
        var tolerance = 7 / scale;
        for (var i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            if (mark.Tool == Tool.Crop) continue;
            var reach = tolerance + mark.Style.Width / 2;
            switch (mark.Tool)
            {
                case Tool.Arrow:
                    if (Distance(p, mark.Points[0], mark.Points[^1]) <= reach) return i;
                    break;
                case Tool.Pen:
                    for (var k = 1; k < mark.Points.Length; k++)
                        if (Distance(p, mark.Points[k - 1], mark.Points[k]) <= reach) return i;
                    break;
                case Tool.Box or Tool.Oval when !mark.Style.Fill:
                    var box = MarkBounds(mark);
                    var outer = Inflate(box, reach);
                    var inner = Inflate(box, -reach);
                    if (outer.Contains(new Point(p.X, p.Y)) && (inner.IsEmpty || !inner.Contains(new Point(p.X, p.Y)))) return i;
                    break;
                default:
                    if (Inflate(MarkBounds(mark), tolerance).Contains(new Point(p.X, p.Y))) return i;
                    break;
            }
        }
        return -1;

        static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.LengthSquared() < 0.001f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }
    }

    private static Rect Inflate(Rect r, double by)
    {
        var width = r.Width + by * 2;
        var height = r.Height + by * 2;
        return width <= 0 || height <= 0 ? Rect.Empty : new Rect(r.X - by, r.Y - by, width, height);
    }

    /// <summary>Resize handles (image coordinates) of a mark: both ends of arrows, four corners of shapes.</summary>
    private Vector2[] Handles(Mark mark) => mark.Tool switch
    {
        Tool.Arrow => [mark.Points[0], mark.Points[^1]],
        Tool.Box or Tool.Oval or Tool.Highlight or Tool.Blur => Corners(MarkBounds(mark)),
        _ => []
    };

    private static Vector2[] Corners(Rect r) =>
    [
        new((float)r.Left, (float)r.Top), new((float)r.Right, (float)r.Top),
        new((float)r.Right, (float)r.Bottom), new((float)r.Left, (float)r.Bottom)
    ];

    // ---------------------------------------------------------------- pointer input

    private void OnPress(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(canvas);
        if (bitmap is null) return;
        canvas.Focus(FocusState.Pointer);
        var position = new Vector2((float)pointer.Position.X, (float)pointer.Position.Y);
        if (pointer.Properties.IsMiddleButtonPressed || (spaceDown && pointer.Properties.IsLeftButtonPressed))
        {
            BeginPan(position);
            canvas.CapturePointer(e.Pointer);
            return;
        }
        if (!pointer.Properties.IsLeftButtonPressed) return;
        if (editingAt is not null) { FinishText(commit: true); return; }
        var p = FromCanvas(pointer.Position);

        if (tool == Tool.Select)
        {
            if (Selected is { } current)
            {
                var corners = Handles(current);
                for (var i = 0; i < corners.Length; i++)
                    if (Vector2.Distance(ToCanvas(corners[i]), position) <= 9)
                    {
                        drag = Drag.Handle;
                        handle = i;
                        handleAnchor = current.Tool == Tool.Arrow ? corners[1 - i] : corners[(i + 2) % 4];
                        dragOriginal = current;
                        canvas.CapturePointer(e.Pointer);
                        return;
                    }
            }
            var hit = HitTest(p);
            if (hit != selection)
            {
                selection = hit;
                BuildInspector();
            }
            if (hit >= 0)
            {
                drag = Drag.Move;
                dragStart = p;
                dragOriginal = marks[hit];
                canvas.CapturePointer(e.Pointer);
            }
            else if (zoom > 0) BeginPan(position);
            canvas.Invalidate();
            return;
        }

        if (!Inflate(PaperBounds(Crop), 0).Contains(new Point(p.X + Crop.X - (PaperBounds(Crop).Width - Crop.Width) / 2,
                p.Y + Crop.Y - (PaperBounds(Crop).Height - Crop.Height) / 2)) && !ImageBounds.Contains(new Point(p.X, p.Y)))
            return;
        p = Vector2.Clamp(p, Vector2.Zero, new Vector2(entry.Width, entry.Height));
        if (tool == Tool.Text)
        {
            BeginText(p, -1);
            return;
        }
        if (tool == Tool.Step)
        {
            Checkpoint(null);
            var number = marks.Count(m => m.Tool == Tool.Step) + 1;
            marks.Add(new Mark(Tool.Step, [p], styles[Tool.Step], number.ToString()));
            UpdateStatus();
            canvas.Invalidate();
            return;
        }
        if (canvas.CapturePointer(e.Pointer))
        {
            drag = Drag.Draw;
            points = [p, p];
        }
    }

    private void BeginPan(Vector2 position)
    {
        if (zoom == 0) { zoom = scale; view = placement; }
        drag = Drag.Pan;
        panStart = position - view;
        canvas.ProtectedCursorShape(InputSystemCursorShape.SizeAll);
    }

    private void OnMove(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(canvas);
        var position = new Vector2((float)pointer.Position.X, (float)pointer.Position.Y);
        if (drag == Drag.None)
        {
            if (tool == Tool.Select && bitmap is not null)
            {
                var overHandle = Selected is { } current && Handles(current).Any(h => Vector2.Distance(ToCanvas(h), position) <= 9);
                var overMark = overHandle || HitTest(FromCanvas(pointer.Position)) >= 0;
                canvas.ProtectedCursorShape(overHandle ? InputSystemCursorShape.Cross
                    : overMark ? InputSystemCursorShape.SizeAll : InputSystemCursorShape.Arrow);
            }
            return;
        }
        var p = FromCanvas(pointer.Position);
        var limit = new Vector2(entry.Width, entry.Height);
        switch (drag)
        {
            case Drag.Pan:
                view = position - panStart;
                break;
            case Drag.Draw when points is not null:
                p = Vector2.Clamp(p, Vector2.Zero, limit);
                if (tool == Tool.Pen)
                {
                    if (Vector2.Distance(points[^1], p) * scale >= 1.5f) points.Add(p);
                }
                else
                {
                    if (IsShift() && tool is Tool.Box or Tool.Oval or Tool.Highlight or Tool.Blur or Tool.Crop)
                    {
                        var side = Math.Max(Math.Abs(p.X - points[0].X), Math.Abs(p.Y - points[0].Y));
                        p = points[0] + new Vector2(MathF.CopySign(side, p.X - points[0].X), MathF.CopySign(side, p.Y - points[0].Y));
                    }
                    if (IsShift() && tool == Tool.Arrow) p = Snap(points[0], p);
                    points[^1] = Vector2.Clamp(p, Vector2.Zero, limit);
                }
                break;
            case Drag.Move when dragOriginal is not null:
                var delta = p - dragStart;
                if (delta.LengthSquared() < 0.01f) break;
                if (ReferenceEquals(marks[selection], dragOriginal)) Checkpoint(null);
                marks[selection] = dragOriginal with { Points = [.. dragOriginal.Points.Select(v => v + delta)] };
                break;
            case Drag.Handle when dragOriginal is not null:
                if (ReferenceEquals(marks[selection], dragOriginal)) Checkpoint(null);
                p = Vector2.Clamp(p, Vector2.Zero, limit);
                if (IsShift() && dragOriginal.Tool == Tool.Arrow) p = Snap(handleAnchor, p);
                marks[selection] = dragOriginal with
                {
                    Points = dragOriginal.Tool == Tool.Arrow && handle == 0 ? [p, handleAnchor] : [handleAnchor, p]
                };
                break;
        }
        canvas.Invalidate();

        static Vector2 Snap(Vector2 from, Vector2 to)
        {
            var v = to - from;
            var angle = MathF.Round(MathF.Atan2(v.Y, v.X) / (MathF.PI / 4)) * (MathF.PI / 4);
            return from + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * v.Length();
        }
    }

    private static bool IsShift() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnRelease(object sender, PointerRoutedEventArgs e)
    {
        if (drag == Drag.None) return;
        if (drag is Drag.Draw or Drag.Move or Drag.Handle) OnMove(sender, e);
        canvas.ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void EndDrag()
    {
        var finished = points?.ToArray();
        var kind = drag;
        drag = Drag.None;
        points = null;
        dragOriginal = null;
        canvas.ProtectedCursorShape(tool == Tool.Select ? InputSystemCursorShape.Arrow : InputSystemCursorShape.Cross);
        if (kind == Drag.Draw && finished is { Length: > 0 })
        {
            var tiny = Vector2.Distance(finished[0], finished[^1]) * scale < 3 && tool != Tool.Pen;
            if (!tiny)
            {
                Checkpoint(null);
                if (tool == Tool.Crop) marks.RemoveAll(m => m.Tool == Tool.Crop);
                var points = tool == Tool.Pen ? Simplify(finished) : [finished[0], finished[^1]];
                marks.Add(new Mark(tool, points, styles[tool]));
                if (tool == Tool.Crop && zoom == 0) AnimateFit();
            }
        }
        UpdateStatus();
        canvas.Invalidate();
    }

    /// <summary>Drops pen samples closer than half a pixel to keep strokes light without visible change.</summary>
    private static Vector2[] Simplify(Vector2[] samples)
    {
        var kept = new List<Vector2> { samples[0] };
        foreach (var sample in samples.Skip(1))
            if (Vector2.Distance(kept[^1], sample) >= .5f) kept.Add(sample);
        if (kept.Count == 1) kept.Add(samples[^1]);
        return [.. kept];
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (tool != Tool.Select) return;
        var hit = HitTest(FromCanvas(e.GetPosition(canvas)));
        if (hit >= 0 && marks[hit].Tool == Tool.Text)
        {
            selection = hit;
            BeginText(marks[hit].Points[0], hit);
        }
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(canvas);
        var delta = pointer.Properties.MouseWheelDelta;
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var at = new Vector2((float)pointer.Position.X, (float)pointer.Position.Y);
        if (control)
            ZoomAround(at, MathF.Pow(1.0018f, delta));
        else if (zoom > 0)
        {
            var horizontal = pointer.Properties.IsHorizontalMouseWheel || IsShift();
            view += horizontal ? new Vector2(-delta * .6f, 0) : new Vector2(0, delta * .6f);
            canvas.Invalidate();
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- zoom

    private void ZoomAround(Vector2 at, float factor)
    {
        if (zoom == 0) { zoom = scale; view = placement; }
        var next = Math.Clamp(zoom * factor, .05f, 16);
        view = at - (at - view) * (next / zoom);
        zoom = next;
        canvas.Invalidate();
    }

    private void ZoomBy(float factor)
    {
        var center = new Vector2((float)(Available().X + Available().Width / 2), (float)(Available().Y + Available().Height / 2));
        var from = zoom == 0 ? scale : zoom;
        AnimateZoom(Math.Clamp(from * factor, .05f, 16), center);
    }

    private void AnimateFit() => AnimateZoom(0);

    private long zoomAnimation;

    /// <summary>Eases the canvas zoom over ~200 ms on the render loop (a frame per display refresh).</summary>
    private void AnimateZoom(float target, Vector2? around = null)
    {
        if (bitmap is null) return;
        var startZoom = zoom == 0 ? scale : zoom;
        var startView = zoom == 0 ? placement : view;
        var anchor = around ?? new Vector2((float)canvas.ActualWidth / 2, (float)canvas.ActualHeight / 2);
        if (!ProductMotion.Enabled)
        {
            zoom = target;
            if (target > 0) view = anchor - (anchor - startView) * (target / startZoom);
            canvas.Invalidate();
            return;
        }
        var id = ++zoomAnimation;
        var started = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(220);
        void Tick(object? sender, object args)
        {
            if (id != zoomAnimation || closed) { CompositionTarget.Rendering -= Tick; return; }
            var t = Math.Clamp((float)((DateTime.UtcNow - started) / duration), 0, 1);
            var eased = 1 - MathF.Pow(1 - t, 3);
            var endZoom = target == 0 ? FitScale() : target;
            var endView = target == 0 ? CenteredPlacement(endZoom) : anchor - (anchor - startView) * (target / startZoom);
            zoom = startZoom + (endZoom - startZoom) * eased;
            view = Vector2.Lerp(startView, endView, eased);
            if (t >= 1)
            {
                CompositionTarget.Rendering -= Tick;
                if (target == 0) zoom = 0;
            }
            canvas.Invalidate();
        }
        CompositionTarget.Rendering += Tick;
    }

    private Vector2 CenteredPlacement(float at)
    {
        var output = PaperBounds(Crop);
        var available = Available();
        return new Vector2((float)(available.X + (available.Width - output.Width * at) / 2),
            (float)(available.Y + (available.Height - output.Height * at) / 2));
    }

    // ---------------------------------------------------------------- keyboard

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(Content.XamlRoot);
        if (editingAt is not null || focused is TextBox or NumberBox or ComboBox) return;
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var onCanvas = focused is null || ReferenceEquals(focused, canvas);
        if (!control && !onCanvas) return;
        var handled = true;
        switch (e.Key)
        {
            case VirtualKey.Z when control && IsShift(): Redo(); break;
            case VirtualKey.Z when control: Undo(); break;
            case VirtualKey.Y when control: Redo(); break;
            case VirtualKey.C when control: _ = Guarded(CopyAsync); break;
            case VirtualKey.S when control: _ = Guarded(() => save(RenderFrame())); break;
            case VirtualKey.Number0 when control: AnimateZoom(0); break;
            case (VirtualKey)187 when control: ZoomBy(1.25f); break;
            case (VirtualKey)189 when control: ZoomBy(1 / 1.25f); break;
            case VirtualKey.Delete or VirtualKey.Back: DeleteSelection(); break;
            case VirtualKey.Escape:
                if (selection >= 0) { selection = -1; BuildInspector(); canvas.Invalidate(); }
                else if (tool != Tool.Select) SelectTool(Tool.Select);
                break;
            case VirtualKey.Space: spaceDown = true; break;
            default:
                handled = false;
                if (!control)
                    foreach (var (kind, _, _, key) in Tools)
                        if (e.Key.ToString() == key) { SelectTool(kind); handled = true; break; }
                break;
        }
        e.Handled = handled;
    }

    // ---------------------------------------------------------------- inline text editing

    private void BeginText(Vector2 at, int index)
    {
        editingAt = at;
        editingIndex = index;
        textEditor.Text = index >= 0 ? marks[index].Text : "";
        if (index >= 0 && marks[index].Typography is { } existing) typography = existing;
        StyleEditor();
        textEditor.Visibility = Visibility.Visible;
        textEditor.Focus(FocusState.Programmatic);
        textEditor.SelectAll();
        canvas.Invalidate();
    }

    private void StyleEditor()
    {
        if (editingAt is not { } at) return;
        var position = ToCanvas(at);
        Canvas.SetLeft(textEditor, position.X - 3);
        Canvas.SetTop(textEditor, position.Y - 2);
        textEditor.FontSize = Math.Max(6, typography.Size * scale);
        textEditor.FontFamily = new FontFamily(typography.Font);
        textEditor.FontWeight = typography.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        textEditor.FontStyle = typography.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        textEditor.Foreground = new SolidColorBrush(StyleOf(Tool.Text).Color);
    }

    private void FinishText(bool commit)
    {
        if (editingAt is not { } at) return;
        editingAt = null;
        var value = textEditor.Text.TrimEnd();
        textEditor.Visibility = Visibility.Collapsed;
        if (commit && value.Length > 0)
        {
            Checkpoint(null);
            var mark = new Mark(Tool.Text, [at], editingIndex >= 0 ? marks[editingIndex].Style : styles[Tool.Text], value, typography);
            if (editingIndex >= 0 && editingIndex < marks.Count) marks[editingIndex] = mark;
            else marks.Add(mark);
        }
        else if (commit && editingIndex >= 0 && editingIndex < marks.Count)
        {
            Checkpoint(null);
            marks.RemoveAt(editingIndex);
            selection = -1;
        }
        editingIndex = -1;
        UpdateStatus();
        canvas.Invalidate();
        canvas.Focus(FocusState.Programmatic);
    }

    // ---------------------------------------------------------------- rendering

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs e)
    {
        if (bitmap is null) return;
        var crop = Crop;
        var paperRect = PaperBounds(crop);
        scale = zoom > 0 ? zoom : FitScale();
        placement = zoom > 0 ? view : CenteredPlacement(scale);
        origin = placement + new Vector2((float)((paperRect.Width - crop.Width) / 2 - crop.X),
            (float)((paperRect.Height - crop.Height) / 2 - crop.Y)) * scale;
        zoomLabel.Text = $"{Math.Round(scale * 100):0}%";
        var ds = e.DrawingSession;
        if (!paperOn)
        {
            using var lift = new CanvasCommandList(ds);
            using (var shape = lift.CreateDrawingSession())
                shape.FillRectangle(new Rect(placement.X, placement.Y, paperRect.Width * scale, paperRect.Height * scale), Colors.Black);
            using var shadow = new ShadowEffect { Source = lift, BlurAmount = 10, ShadowColor = Color.FromArgb(110, 0, 0, 0) };
            ds.DrawImage(shadow, 0, 4);
        }
        ds.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(placement);
        Draw(ds, crop, preview: true);
        ds.Transform = Matrix3x2.Identity;
        DrawSelection(ds);
        if (editingAt is not null) StyleEditor();
    }

    private void Draw(CanvasDrawingSession ds, Rect crop, bool preview)
    {
        var paperRect = PaperBounds(crop);
        var cutout = new Rect((paperRect.Width - crop.Width) / 2, (paperRect.Height - crop.Height) / 2, crop.Width, crop.Height);
        var radius = paperOn ? (float)paperRadius : 0;
        var transform = ds.Transform;
        if (paperOn)
        {
            ds.FillRectangle(paperRect, paper);
            if (paperShadow)
            {
                using var lift = new CanvasCommandList(ds);
                using (var shape = lift.CreateDrawingSession()) shape.FillRoundedRectangle(cutout, radius, radius, Colors.Black);
                using var shadow = new ShadowEffect { Source = lift, BlurAmount = 18, ShadowColor = Color.FromArgb(120, 0, 0, 0) };
                ds.DrawImage(shadow, 0, 10);
            }
        }
        using var mask = CanvasGeometry.CreateRoundedRectangle(ds, cutout, radius, radius);
        using (ds.CreateLayer(1, mask))
        {
            ds.Transform = Matrix3x2.CreateTranslation((float)(cutout.X - crop.X), (float)(cutout.Y - crop.Y)) * transform;
            ds.DrawImage(bitmap!, 0, 0);
            foreach (var mark in marks)
                if (mark.Tool != Tool.Crop) DrawMark(ds, mark, bitmap!);
            if (preview && points is { Count: > 0 } && drag == Drag.Draw && tool != Tool.Crop)
                DrawMark(ds, new Mark(tool, [.. points], styles[tool]), bitmap!);
        }
        if (preview && drag == Drag.Draw && tool == Tool.Crop && points is { Count: > 0 })
        {
            ds.Transform = Matrix3x2.CreateTranslation((float)(cutout.X - crop.X), (float)(cutout.Y - crop.Y)) * transform;
            var area = Box(points[0], points[^1]);
            using var outside = CanvasGeometry.CreateRectangle(ds, crop).CombineWith(CanvasGeometry.CreateRectangle(ds, area),
                Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            ds.FillGeometry(outside, Color.FromArgb(140, 0, 0, 0));
            ds.DrawRectangle(area, Colors.White, 1.5f / scale);
        }
        ds.Transform = transform;
    }

    private void DrawSelection(CanvasDrawingSession ds)
    {
        if (tool != Tool.Select || Selected is not { } mark || editingAt is not null) return;
        var r = MarkBounds(mark);
        var a = ToCanvas(new Vector2((float)r.X, (float)r.Y));
        var b = ToCanvas(new Vector2((float)r.Right, (float)r.Bottom));
        using var dashed = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
        var outline = new Rect(a.X - 4, a.Y - 4, b.X - a.X + 8, b.Y - a.Y + 8);
        ds.DrawRoundedRectangle(outline, 4, 4, Color.FromArgb(200, 255, 255, 255), 1, dashed);
        foreach (var handle in Handles(mark))
        {
            var c = ToCanvas(handle);
            ds.FillCircle(c, 5.5f, Colors.White);
            ds.DrawCircle(c, 5.5f, Color.FromArgb(160, 0, 0, 0), 1);
        }
    }

    private static void DrawMark(CanvasDrawingSession ds, Mark mark, CanvasBitmap source)
    {
        if (mark.Tool == Tool.Blur)
        {
            DrawBlur(ds, mark, source);
            return;
        }
        using var layer = mark.Style.Opacity < .999f ? ds.CreateLayer(mark.Style.Opacity) : null;
        if (mark.Style.Shadow && mark.Tool != Tool.Highlight)
        {
            // Built in image space and drawn under the current transform: identical in preview, copy and export.
            using var list = new CanvasCommandList(ds);
            using (var shape = list.CreateDrawingSession()) Shape(shape, mark);
            using var shadow = new ShadowEffect { Source = list, BlurAmount = 3.2f, ShadowColor = Color.FromArgb(120, 0, 0, 0) };
            ds.DrawImage(shadow, 0, 2.2f);
        }
        Shape(ds, mark);
    }

    private static void Shape(CanvasDrawingSession ds, Mark mark)
    {
        var a = mark.Points[0];
        var b = mark.Points[^1];
        var color = mark.Style.Color;
        var width = mark.Style.Width;
        using var stroke = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, DashCap = CanvasCapStyle.Round,
            LineJoin = CanvasLineJoin.Round,
            DashStyle = mark.Style.Dash switch { 1 => CanvasDashStyle.Dash, 2 => CanvasDashStyle.Dot, _ => CanvasDashStyle.Solid }
        };
        switch (mark.Tool)
        {
            case Tool.Arrow:
                if (Vector2.Distance(a, b) < 1) break;
                var axis = Vector2.Normalize(b - a);
                var normal = new Vector2(-axis.Y, axis.X);
                var headLength = Math.Min(Vector2.Distance(a, b) * .45f, width * 3.4f + 6);
                var headWidth = headLength * .62f;
                switch (mark.Style.Head)
                {
                    case 1:
                        var bas = b - axis * headLength;
                        ds.DrawLine(a, bas + axis * .5f, color, width, stroke);
                        using (var builder = new CanvasPathBuilder(ds))
                        {
                            builder.BeginFigure(b + axis * width * .3f);
                            builder.AddLine(bas + normal * headWidth);
                            builder.AddLine(bas + axis * headLength * .18f);
                            builder.AddLine(bas - normal * headWidth);
                            builder.EndFigure(CanvasFigureLoop.Closed);
                            using var head = CanvasGeometry.CreatePath(builder);
                            ds.FillGeometry(head, color);
                            using var round = new CanvasStrokeStyle { LineJoin = CanvasLineJoin.Round };
                            ds.DrawGeometry(head, color, width * .45f, round);
                        }
                        break;
                    case 2:
                        ds.DrawLine(a, b, color, width, stroke);
                        ds.FillCircle(b, width * 1.35f + 2, color);
                        break;
                    default:
                        ds.DrawLine(a, b, color, width, stroke);
                        using (var solid = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round })
                        {
                            var tail = b - axis * headLength;
                            ds.DrawLine(b, tail + normal * headWidth, color, width, solid);
                            ds.DrawLine(b, tail - normal * headWidth, color, width, solid);
                        }
                        break;
                }
                break;
            case Tool.Box:
                var box = Box(a, b);
                var corner = Math.Min(width * 1.2f + 2, (float)Math.Min(box.Width, box.Height) / 2);
                if (mark.Style.Fill) ds.FillRoundedRectangle(box, corner, corner, Color.FromArgb(70, color.R, color.G, color.B));
                ds.DrawRoundedRectangle(box, corner, corner, color, width, stroke);
                break;
            case Tool.Oval:
                var oval = Box(a, b);
                var center = new Vector2((float)(oval.X + oval.Width / 2), (float)(oval.Y + oval.Height / 2));
                if (mark.Style.Fill) ds.FillEllipse(center, (float)oval.Width / 2, (float)oval.Height / 2, Color.FromArgb(70, color.R, color.G, color.B));
                ds.DrawEllipse(center, (float)oval.Width / 2, (float)oval.Height / 2, color, width, stroke);
                break;
            case Tool.Pen:
                if (mark.Points.Length < 3)
                {
                    ds.DrawLine(a, b, color, width, stroke);
                    break;
                }
                using (var builder = new CanvasPathBuilder(ds))
                {
                    builder.BeginFigure(mark.Points[0]);
                    for (var i = 1; i < mark.Points.Length - 1; i++)
                        builder.AddQuadraticBezier(mark.Points[i], (mark.Points[i] + mark.Points[i + 1]) / 2);
                    builder.AddLine(mark.Points[^1]);
                    builder.EndFigure(CanvasFigureLoop.Open);
                    using var path = CanvasGeometry.CreatePath(builder);
                    ds.DrawGeometry(path, color, width, stroke);
                }
                break;
            case Tool.Highlight:
                ds.FillRoundedRectangle(Box(a, b), 3, 3, color);
                break;
            case Tool.Text:
                DrawText(ds, mark);
                break;
            case Tool.Step:
                var radius = width;
                ds.FillCircle(a, radius, color);
                ds.DrawCircle(a, radius, Color.FromArgb(90, 255, 255, 255), Math.Max(1, radius * .08f));
                var light = .2126 * color.R + .7152 * color.G + .0722 * color.B > 160;
                using (var format = new CanvasTextFormat
                {
                    FontFamily = "Segoe UI Variable Display", FontSize = radius * 1.05f, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center, VerticalAlignment = CanvasVerticalAlignment.Center
                })
                    ds.DrawText(mark.Text, new Rect(a.X - radius, a.Y - radius * 1.06, radius * 2, radius * 2),
                        light ? Color.FromArgb(255, 18, 18, 20) : Colors.White, format);
                break;
        }
    }

    private static CanvasTextLayout TextLayout(ICanvasResourceCreator creator, Mark mark)
    {
        var style = mark.Typography!;
        using var format = new CanvasTextFormat
        {
            FontFamily = style.Font, FontSize = style.Size, WordWrapping = CanvasWordWrapping.NoWrap,
            FontWeight = style.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal
        };
        var layout = new CanvasTextLayout(creator, mark.Text, format, 0, 0);
        if (style.Underline) layout.SetUnderline(0, mark.Text.Length, true);
        return layout;
    }

    private static void DrawText(CanvasDrawingSession ds, Mark mark)
    {
        var style = mark.Typography!;
        var position = mark.Points[0];
        using var layout = TextLayout(ds, mark);
        if (style.Background.A != 0)
        {
            var bounds = layout.LayoutBounds;
            var inset = style.Size * .28f;
            ds.FillRoundedRectangle(new Rect(position.X + bounds.X - inset, position.Y + bounds.Y - inset * .6,
                bounds.Width + 2 * inset, bounds.Height + 1.2 * inset), inset, inset, style.Background);
        }
        if (style.Stroke > 0)
        {
            using var geometry = CanvasGeometry.CreateText(layout);
            var contrast = 0.2126 * mark.Style.Color.R + 0.7152 * mark.Style.Color.G + 0.0722 * mark.Style.Color.B > 128
                ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
            using var round = new CanvasStrokeStyle { LineJoin = CanvasLineJoin.Round };
            ds.DrawGeometry(geometry, position, contrast, style.Stroke * 2, round);
        }
        ds.DrawTextLayout(layout, position, mark.Style.Color);
    }

    /// <summary>Pixelate, blur or redact a region using the original pixels underneath.</summary>
    private static void DrawBlur(CanvasDrawingSession ds, Mark mark, CanvasBitmap source)
    {
        var region = Box(mark.Points[0], mark.Points[^1]);
        region.Intersect(new Rect(0, 0, source.SizeInPixels.Width, source.SizeInPixels.Height));
        if (region.IsEmpty || region.Width < 1 || region.Height < 1) return;
        var area = new Rect(region.X, region.Y, region.Width, region.Height);
        switch (mark.Style.Mode)
        {
            case 2:
                ds.FillRectangle(area, Color.FromArgb(255, 14, 14, 16));
                break;
            case 1:
                using (var crop = new CropEffect { Source = source, SourceRectangle = area })
                using (var blur = new GaussianBlurEffect { Source = crop, BlurAmount = mark.Style.Width * .9f, BorderMode = EffectBorderMode.Hard })
                using (var clip = new CropEffect { Source = blur, SourceRectangle = area })
                    ds.DrawImage(clip);
                break;
            default:
                var cell = Math.Max(3, mark.Style.Width);
                using (var crop = new CropEffect { Source = source, SourceRectangle = area })
                using (var down = new ScaleEffect { Source = crop, Scale = new Vector2(1 / cell), InterpolationMode = CanvasImageInterpolation.Linear, BorderMode = EffectBorderMode.Hard })
                using (var up = new ScaleEffect { Source = down, Scale = new Vector2(cell), InterpolationMode = CanvasImageInterpolation.NearestNeighbor, BorderMode = EffectBorderMode.Hard })
                using (var clip = new CropEffect { Source = up, SourceRectangle = area })
                    ds.DrawImage(clip);
                break;
        }
    }

    // ---------------------------------------------------------------- output

    private CaptureFrame RenderFrame()
    {
        if (editingAt is not null) FinishText(commit: true);
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
        return new CaptureFrame(width, height, target.GetPixelBytes(), HasAlpha: !paperOn);
    }

    /// <summary>Keyboard path for output commands: same guard as the buttons (loaded image, errors as a toast).</summary>
    private async Task Guarded(Func<Task> run)
    {
        if (bitmap is null) return;
        try { await run(); }
        catch (Exception ex) { ToastWindow.Show("Action failed", ex.Message, Glyph.Close); }
    }

    private async Task CopyAsync()
    {
        using var png = await RenderFrame().EncodeAsync();
        var data = new DataPackage();
        data.SetBitmap(RandomAccessStreamReference.CreateFromStream(png));
        Clipboard.SetContent(data);
        Clipboard.Flush();
        ToastWindow.Show("Screenshot copied", "Image is in your clipboard.");
    }
}

internal static class CanvasCursorExtensions
{
    private static readonly System.Reflection.PropertyInfo? cursor =
        typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>Sets the pointer shape over a control (WinUI exposes the cursor only to subclasses).</summary>
    internal static void ProtectedCursorShape(this UIElement element, InputSystemCursorShape shape) =>
        cursor?.SetValue(element, InputSystemCursor.Create(shape));
}
