using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RealCaptureX;

internal enum ProductTheme { System, Light, Dark }
internal enum ButtonTone { Quiet, Raised, Primary, Selected, Danger }

internal static class Appearance
{
    // Neutral graphite / paper palette. Colour lives in annotations, never in the chrome.
    internal static readonly SolidColorBrush Surface = Brush(27, 28, 31);
    internal static readonly SolidColorBrush Chrome = Brush(27, 28, 31, 235);
    internal static readonly SolidColorBrush Panel = Brush(36, 37, 41);
    internal static readonly SolidColorBrush Elevated = Brush(36, 37, 41, 245);
    internal static readonly SolidColorBrush Canvas = Brush(17, 18, 20);
    internal static readonly SolidColorBrush Text = Brush(245, 245, 247);
    internal static readonly SolidColorBrush Muted = Brush(160, 162, 170);
    internal static readonly SolidColorBrush Faint = Brush(110, 112, 120);
    internal static readonly SolidColorBrush Accent = Brush(245, 245, 247);
    private static readonly SolidColorBrush AccentHover = Brush(255, 255, 255);
    private static readonly SolidColorBrush AccentPress = Brush(214, 215, 219);
    private static readonly SolidColorBrush SelectionLine = Brush(255, 255, 255, 34);
    internal static readonly SolidColorBrush OnAccent = Brush(18, 19, 21);
    internal static readonly SolidColorBrush AccentText = Brush(255, 255, 255);
    internal static readonly SolidColorBrush Tint = Brush(255, 255, 255, 22);
    internal static readonly SolidColorBrush Line = Brush(255, 255, 255, 20);
    internal static readonly SolidColorBrush Field = Brush(255, 255, 255, 12);
    internal static readonly SolidColorBrush Danger = Brush(255, 120, 112);
    internal static readonly SolidColorBrush Clear = Brush(0, 0, 0, 0);
    internal static readonly SolidColorBrush GlassTint = Brush(30, 31, 35, 208);
    internal static readonly SolidColorBrush Hover = Brush(255, 255, 255, 16);
    internal static readonly SolidColorBrush Pressed = Brush(255, 255, 255, 10);

    private static readonly SolidColorBrush Rim = Brush(255, 255, 255, 30);
    private static readonly GradientStop SelectionTop = new() { Offset = 0 };
    private static readonly GradientStop SelectionBottom = new() { Offset = 1 };
    private static readonly LinearGradientBrush Selection = new()
    {
        EndPoint = new Windows.Foundation.Point(0, 1),
        GradientStops = { SelectionTop, SelectionBottom }
    };
    internal static Brush SelectedFill => Selection;
    internal static Brush SelectedRim => SelectionLine;
    private static readonly GradientStop RimTop = new() { Offset = 0 };
    private static readonly GradientStop RimBottom = new() { Offset = 1 };
    /// <summary>Hairline rim of glass panels: a touch brighter on top, like light catching an edge.</summary>
    internal static readonly LinearGradientBrush GlassRim = new()
    {
        EndPoint = new Windows.Foundation.Point(0, 1),
        GradientStops = { RimTop, RimBottom }
    };

    private static readonly UISettings ui = new();
    private static readonly FontFamily normal = new("Segoe UI Variable Text");
    private static readonly FontFamily heading = new("Segoe UI Variable Display");
    private static readonly Dictionary<Window, (FrameworkElement Root, bool Caption, bool Floating)> windows = [];
    private static ProductTheme preference;
    private static ElementTheme theme = ElementTheme.Dark;

    private static bool translucency = true;
    private static bool reduceMotion;

    private static bool backdropFailed;

    internal static bool AnimationsEnabled => ui.AnimationsEnabled && !reduceMotion;

    internal static void BackdropUnavailable()
    {
        if (backdropFailed) return;
        backdropFailed = true;
        Refresh();
    }

    /// <summary>App-level preferences layered on top of the Windows settings (which always win).</summary>
    internal static void SetPreferences(bool allowTranslucency, bool reduce)
    {
        translucency = allowTranslucency;
        reduceMotion = reduce;
        Refresh();
    }
    internal static bool IsLight => theme == ElementTheme.Light;

    internal static void Initialize(DispatcherQueue dispatcher)
    {
        ui.ColorValuesChanged += (_, _) => dispatcher.TryEnqueue(Refresh);
        ui.AdvancedEffectsEnabledChanged += (_, _) => dispatcher.TryEnqueue(Refresh);
        Refresh();
    }

    internal static void SetTheme(ProductTheme value)
    {
        preference = value;
        Refresh();
    }

    internal static void Refresh()
    {
        var system = ui.GetColorValue(UIColorType.Background);
        var light = preference == ProductTheme.Light ||
            (preference == ProductTheme.System && system.R + system.G + system.B > 3 * 127);
        var effects = ui.AdvancedEffectsEnabled && translucency && !backdropFailed;
        theme = light ? ElementTheme.Light : ElementTheme.Dark;
        Set(Surface, 0xFF1B1C1F, 0xFFF7F7F8);
        Set(Chrome, effects ? 0xEB1B1C1Fu : 0xFF1B1C1Fu, effects ? 0xEDF7F7F8u : 0xFFF7F7F8u);
        Set(Panel, 0xFF242529, 0xFFFFFFFF);
        Set(Elevated, 0xF5232428, 0xF7FFFFFF);
        Set(Canvas, 0xFF111214, 0xFFE9E9EC);
        Set(Text, 0xFFF5F5F7, 0xFF1A1B1E);
        Set(Muted, 0xFFA0A2AA, 0xFF6A6D75);
        Set(Faint, 0xFF6E7078, 0xFFA3A5AC);
        Set(Accent, 0xFFF5F5F7, 0xFF1D1E22);
        Set(AccentHover, 0xFFFFFFFF, 0xFF34363C);
        Set(AccentPress, 0xFFD6D7DB, 0xFF0F1012);
        Set(SelectionLine, 0x22FFFFFF, 0x14000000);
        Set(OnAccent, 0xFF121315, 0xFFFFFFFF);
        Set(AccentText, 0xFFFFFFFF, 0xFF1A1B1E);
        Set(Tint, 0x16FFFFFF, 0x0D000000);
        Set(Line, 0x14FFFFFF, 0x14000000);
        Set(Field, 0x0CFFFFFF, 0x08000000);
        Set(Hover, 0x10FFFFFF, 0x0A000000);
        Set(Pressed, 0x08FFFFFF, 0x12000000);
        Set(Rim, 0x1EFFFFFF, 0x1A000000);
        Set(GlassTint, effects ? 0xD01E1F23u : 0xFF1E1F23u, effects ? 0xD8F8F8FAu : 0xFFF8F8FAu);
        RimTop.Color = light ? Color.FromArgb(0x24, 0, 0, 0) : Color.FromArgb(0x30, 255, 255, 255);
        RimBottom.Color = light ? Color.FromArgb(0x1C, 0, 0, 0) : Color.FromArgb(0x12, 255, 255, 255);
        SelectionTop.Color = light ? Color.FromArgb(0x16, 0, 0, 0) : Color.FromArgb(0x26, 255, 255, 255);
        SelectionBottom.Color = light ? Color.FromArgb(0x0E, 0, 0, 0) : Color.FromArgb(0x18, 255, 255, 255);
        Set(Danger, 0xFFFF7870, 0xFFC4302B);

        if (NativeWindow.IsHighContrast())
        {
            var foreground = ui.GetColorValue(UIColorType.Foreground);
            Surface.Color = Chrome.Color = Elevated.Color = Panel.Color = Canvas.Color = Tint.Color = OnAccent.Color = GlassTint.Color = system;
            Text.Color = Muted.Color = Faint.Color = Accent.Color = AccentText.Color = Line.Color = Danger.Color = foreground;
            AccentHover.Color = AccentPress.Color = SelectionLine.Color = foreground;
            // Translucent so a hovered or pressed button keeps its label readable in high contrast.
            Hover.Color = Color.FromArgb(0x33, foreground.R, foreground.G, foreground.B);
            Pressed.Color = Color.FromArgb(0x22, foreground.R, foreground.G, foreground.B);
            Rim.Color = RimTop.Color = RimBottom.Color = foreground;
            SelectionTop.Color = SelectionBottom.Color = system;
            theme = ElementTheme.Default;
        }

        if (Application.Current?.Resources is { } resources)
        {
            Shared("HoverFill", Hover.Color);
            Shared("PressFill", Pressed.Color);
            Shared("FocusRing", Text.Color);
            Shared("TipFill", light ? Color.FromArgb(0xF7, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xF5, 0x26, 0x27, 0x2B));
            Shared("TipText", Text.Color);
            Shared("TipLine", Line.Color);

            void Shared(string key, Color color)
            {
                if (resources.TryGetValue(key, out var value) && value is SolidColorBrush brush) brush.Color = color;
            }
        }

        foreach (var (window, (root, caption, floating)) in windows)
        {
            ApplyTheme(root, floating);
            if (caption) StyleTitleBar(window);
        }

        void Set(SolidColorBrush brush, uint darkValue, uint lightValue)
        {
            var value = light ? lightValue : darkValue;
            brush.Color = Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
    }

    internal static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) =>
        new(Color.FromArgb(a, r, g, b));

    internal static TextBlock Label(string value, double size = 13, Brush? brush = null, bool strong = false) =>
        new()
        {
            Text = value, FontSize = size, FontFamily = size >= 20 ? heading : normal, Foreground = brush ?? Text,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, CharacterSpacing = Tracking(size)
        };

    /// <summary>
    /// Size-specific tracking (1/1000 em): display sizes tighten as they grow, captions open up slightly for
    /// legibility, body text stays at the typeface's own spacing.
    /// </summary>
    internal static int Tracking(double size) => size switch
    {
        >= 28 => -18,
        >= 20 => -12,
        >= 15 => -6,
        <= 11 => 12,
        _ => 0
    };

    /// <summary>Keycap label, e.g. "Ctrl ⇧ 1".</summary>
    internal static Border Key(string key) => new()
    {
        Child = Label(key, 11, Muted), Padding = new Thickness(6, 1, 6, 2), MinWidth = 20,
        Background = Field, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center
    };

    internal static string ShortcutText(string binding) => string.Join(" ", binding.Split('+', StringSplitOptions.TrimEntries)
        .Select(part => part.Equals("Shift", StringComparison.OrdinalIgnoreCase) ? "⇧" : part));

    internal static Border Mark(double size = 30) => Badge(Glyph.Mark, size);

    internal static Border Badge(Glyph glyph, double size = 30) => new()
    {
        Width = size, Height = size, Background = Tint, CornerRadius = new CornerRadius(size / 3.2),
        Child = Symbols.Draw(glyph, size * 0.62, AccentText), VerticalAlignment = VerticalAlignment.Center
    };

    internal static Border Frame(FrameworkElement child, double radius = 12) => new()
    {
        Child = child, Background = Surface, BorderBrush = Rim, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius)
    };

    /// <summary>Raised in-window card (editor tool bar, properties panel) with the shared soft shadow.</summary>
    internal static Grid Card(FrameworkElement content, double radius = 14)
    {
        var host = new Grid();
        var shadow = new Grid { IsHitTestVisible = false, Margin = new Thickness(-GlassSurface.Margin) };
        GlassShadow.Attach(shadow, radius, GlassSurface.Margin);
        host.Children.Add(shadow);
        host.Children.Add(new Border
        {
            Child = content, Background = Elevated, BorderBrush = GlassRim, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius)
        });
        return host;
    }

    /// <summary>Hosts a floating window's content on the shared glass material.</summary>
    internal static GlassSurface Float(Window window, FrameworkElement content, double radius = 14)
    {
        var glass = new GlassSurface(window, content, radius);
        SetContent(window, glass.Root, floating: true);
        return glass;
    }

    internal static Button Action(string label, Action? execute = null, Glyph? icon = null, ButtonTone tone = ButtonTone.Quiet)
    {
        var button = new Button
        {
            Height = 34, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8),
            Style = (Style)Application.Current.Resources["CaptureButton"],
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        SetTone(button, tone);
        button.Content = icon is { } glyph ? ActionContent(label, glyph, button.Foreground) : label;

        AutomationProperties.SetName(button, label);
        if (execute is not null) button.Click += (_, _) => execute();
        return button;
    }

    internal static StackPanel ActionContent(string label, Glyph glyph, Brush ink)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(Symbols.Draw(glyph, 16, ink));
        row.Children.Add(Label(label, 12, ink, strong: true));
        return row;
    }

    internal static Button IconButton(Glyph glyph, string name, Action? execute = null, double size = 33)
    {
        var button = Action(name, execute);
        button.Width = size;
        button.Height = size;
        button.Padding = new Thickness(0);
        button.Content = Symbols.Draw(glyph, size >= 34 ? 18 : 16);
        ToolTipService.SetToolTip(button, name);
        return button;
    }

    internal static void SetTone(Button button, ButtonTone tone)
    {
        button.Background = tone switch
        {
            ButtonTone.Primary => Accent, ButtonTone.Selected => Selection, ButtonTone.Raised => Tint, _ => Clear
        };
        button.Foreground = tone switch
        {
            ButtonTone.Primary => OnAccent, ButtonTone.Selected => AccentText, ButtonTone.Danger => Danger, _ => Text
        };
        button.BorderBrush = tone == ButtonTone.Selected ? SelectionLine : Line;
        button.BorderThickness = new Thickness(tone is ButtonTone.Raised or ButtonTone.Selected ? 1 : 0);
    }

    internal static Border Divider() => new()
    {
        Width = 1, Height = 22, Background = Line, Margin = new Thickness(4, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    internal static Border Rule() => new() { Height = 1, Background = Line };

    internal static void SetContent(Window window, FrameworkElement root, bool floating = false, string? caption = null)
    {
        ApplyNativeAccents(root.Resources, Accent, AccentHover, AccentPress, OnAccent);
        if (caption is not null)
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base };
            var shell = new Grid { Background = Chrome };
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            shell.RowDefinitions.Add(new RowDefinition());
            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, Background = Clear,
                Padding = new Thickness(14, 0, 138, 0)
            };
            title.Children.Add(Mark(26));
            title.Children.Add(Label("REAL Capture X", 13, Text, strong: true));
            title.Children.Add(new Border { Width = 1, Height = 14, Background = Line, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(Label(caption, 13, Muted));
            shell.Children.Add(title);
            Grid.SetRow(root, 1);
            shell.Children.Add(root);
            root = shell;
            window.ExtendsContentIntoTitleBar = true;
            window.Content = root;
            window.SetTitleBar(title);
            StyleTitleBar(window);
        }
        else window.Content = root;

        ApplyTheme(root, floating);
        windows.Add(window, (root, caption is not null, floating));
        window.Closed += (_, _) => windows.Remove(window);
    }

    /// <summary>
    /// Floating glass windows keep their XAML root on the dark theme and theme their children instead: with a
    /// Light root, WinUI fills the whole window, including the transparent shadow margin, with opaque white
    /// (measured 2026-11-27: a white box around every light toolbar, menu and card).
    /// </summary>
    private static void ApplyTheme(FrameworkElement root, bool floating)
    {
        if (!floating || root is not Panel panel)
        {
            root.RequestedTheme = theme;
            return;
        }
        root.RequestedTheme = ElementTheme.Dark;
        foreach (var child in panel.Children)
            if (child is FrameworkElement element) element.RequestedTheme = theme;
    }

    internal static void ApplyNativeAccents(ResourceDictionary resources, SolidColorBrush accent,
        SolidColorBrush hover, SolidColorBrush press, SolidColorBrush contrast)
    {
        Add(accent, "SliderThumbBackground", "SliderTrackValueFill", "ToggleSwitchFillOn", "ToggleSwitchStrokeOn",
            "TextControlBorderBrushFocused", "ComboBoxBorderBrushFocused",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked", "ListViewItemSelectionIndicatorBrush");
        Add(hover, "SliderThumbBackgroundPointerOver", "SliderTrackValueFillPointerOver", "ToggleSwitchFillOnPointerOver",
            "ToggleSwitchStrokeOnPointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "ListViewItemSelectionIndicatorPointerOverBrush");
        Add(press, "SliderThumbBackgroundPressed", "SliderTrackValueFillPressed", "ToggleSwitchFillOnPressed",
            "ToggleSwitchStrokeOnPressed", "CheckBoxCheckBackgroundFillCheckedPressed",
            "CheckBoxCheckBackgroundStrokeCheckedPressed", "ListViewItemSelectionIndicatorPressedBrush");
        Add(contrast, "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
            "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed");

        void Add(SolidColorBrush brush, params string[] keys)
        {
            foreach (var key in keys) resources[key] = brush;
        }
    }

    private static void StyleTitleBar(Window window)
    {
        var title = window.AppWindow.TitleBar;
        title.BackgroundColor = Surface.Color;
        title.ButtonBackgroundColor = title.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        title.ForegroundColor = title.ButtonForegroundColor = Text.Color;
        title.InactiveBackgroundColor = Surface.Color;
        title.InactiveForegroundColor = title.ButtonInactiveForegroundColor = Muted.Color;
        title.ButtonHoverBackgroundColor = Hover.Color;
        title.ButtonPressedBackgroundColor = Pressed.Color;
        title.ButtonHoverForegroundColor = title.ButtonPressedForegroundColor = Text.Color;
    }

}
