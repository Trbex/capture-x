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
    internal static readonly SolidColorBrush Surface = Brush(30, 30, 33);
    internal static readonly SolidColorBrush Glass = Brush(30, 30, 33, 241);
    internal static readonly SolidColorBrush Panel = Brush(38, 38, 42);
    internal static readonly SolidColorBrush Canvas = Brush(21, 21, 24);
    internal static readonly SolidColorBrush Text = Brush(247, 244, 241);
    internal static readonly SolidColorBrush Muted = Brush(181, 176, 173);
    internal static readonly SolidColorBrush Accent = Brush(250, 178, 117);
    private static readonly SolidColorBrush AccentHover = Brush(255, 195, 141);
    private static readonly SolidColorBrush AccentPress = Brush(228, 146, 86);
    private static readonly SolidColorBrush SelectionLine = Brush(173, 120, 83);
    internal static readonly SolidColorBrush OnAccent = Brush(46, 24, 11);
    internal static readonly SolidColorBrush AccentText = Brush(255, 202, 158);
    internal static readonly SolidColorBrush Tint = Brush(63, 43, 33);
    internal static readonly SolidColorBrush Line = Brush(62, 59, 61);
    internal static readonly SolidColorBrush Danger = Brush(251, 142, 135);
    internal static readonly SolidColorBrush Clear = Brush(0, 0, 0, 0);

    private static readonly UISettings ui = new();
    private static readonly FontFamily normal = new("Segoe UI Variable Text");
    private static readonly FontFamily heading = new("Segoe UI Variable Display");
    private static readonly Dictionary<Window, (FrameworkElement Root, bool Caption, bool Floating)> windows = [];
    private static ProductTheme preference;
    private static ElementTheme theme = ElementTheme.Dark;

    internal static bool AnimationsEnabled => ui.AnimationsEnabled;

    internal static void Initialize(DispatcherQueue dispatcher)
    {
        ui.ColorValuesChanged += (_, _) => dispatcher.TryEnqueue(Refresh);
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
        theme = light ? ElementTheme.Light : ElementTheme.Dark;
        Set(Surface, 0xFF1E1E21, 0xFFFBFAF7);
        Set(Glass, 0xF11E1E21, 0xF2FBFAF7);
        Set(Panel, 0xFF28282C, 0xFFF2EFE9);
        Set(Canvas, 0xFF151517, 0xFFEAE7E1);
        Set(Text, 0xFFF7F4F1, 0xFF292523);
        Set(Muted, 0xFFB7B0AA, 0xFF635B54);
        Set(Accent, 0xFFFAB275, 0xFF9E451C);
        Set(AccentHover, 0xFFFFC38D, 0xFFAD5026);
        Set(AccentPress, 0xFFE49256, 0xFF803815);
        Set(SelectionLine, 0xFFAD7853, 0xFFC0886D);
        Set(OnAccent, 0xFF2E180B, 0xFFFFFAF5);
        Set(AccentText, 0xFFFFCA9E, 0xFF81360F);
        Set(Tint, 0xFF3F2B21, 0xFFFBE8D8);
        Set(Line, 0xFF413D3E, 0xFFDDD6CD);
        Set(Danger, 0xFFFB8E87, 0xFFAA2E2B);

        if (NativeWindow.IsHighContrast())
        {
            var foreground = ui.GetColorValue(UIColorType.Foreground);
            Surface.Color = Glass.Color = Panel.Color = Canvas.Color = Tint.Color = OnAccent.Color = system;
            Text.Color = Muted.Color = Accent.Color = AccentText.Color = Line.Color = Danger.Color = foreground;
            AccentHover.Color = AccentPress.Color = SelectionLine.Color = foreground;
            theme = ElementTheme.Default;
        }

        foreach (var (window, (root, caption, floating)) in windows)
        {
            root.RequestedTheme = theme;
            if (caption) StyleTitleBar(window);
            if (floating) NativeWindow.StyleFloat(window, Line.Color, !light);
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
            VerticalAlignment = VerticalAlignment.Center
        };

    internal static Border Key(string key) => new()
    {
        Child = Label(key, 10, Muted, strong: true), Padding = new Thickness(5, 2, 5, 2),
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center
    };

    internal static Border Mark(double size = 30) => Badge(Glyph.Mark, size);

    internal static Border Badge(Glyph glyph, double size = 30) => new()
    {
        Width = size, Height = size, Background = Tint, CornerRadius = new CornerRadius(size / 3.2),
        Child = Symbols.Draw(glyph, size * 0.62, AccentText), VerticalAlignment = VerticalAlignment.Center
    };

    internal static Border Frame(FrameworkElement child, double radius = 14) => new()
    {
        Child = child, Background = Glass, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius)
    };

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
            ButtonTone.Primary => Accent, ButtonTone.Selected => Tint, ButtonTone.Raised => Panel, _ => Clear
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
        if (floating)
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
            NativeWindow.StyleFloat(window, Line.Color, theme != ElementTheme.Light);
            if (root is Border frame) frame.CornerRadius = new CornerRadius(8);
        }
        ApplyNativeAccents(root.Resources, Accent, AccentHover, AccentPress, OnAccent);
        if (caption is not null)
        {
            var shell = new Grid { Background = Surface };
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            shell.RowDefinitions.Add(new RowDefinition());
            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 9, Background = Surface,
                Padding = new Thickness(13, 0, 138, 0)
            };
            title.Children.Add(Mark(25));
            title.Children.Add(Label(caption, 12, Text, strong: true));
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

        root.RequestedTheme = theme;
        windows.Add(window, (root, caption is not null, floating));
        window.Closed += (_, _) => windows.Remove(window);
    }

    internal static void ApplyNativeAccents(ResourceDictionary resources, SolidColorBrush accent,
        SolidColorBrush hover, SolidColorBrush press, SolidColorBrush contrast)
    {
        Add(accent, "SliderThumbBackground", "SliderTrackValueFill", "ToggleSwitchFillOn", "ToggleSwitchStrokeOn",
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
        title.BackgroundColor = title.ButtonBackgroundColor = Surface.Color;
        title.ForegroundColor = title.ButtonForegroundColor = Text.Color;
        title.InactiveBackgroundColor = title.ButtonInactiveBackgroundColor = Surface.Color;
        title.InactiveForegroundColor = title.ButtonInactiveForegroundColor = Muted.Color;
        title.ButtonHoverBackgroundColor = Line.Color;
        title.ButtonPressedBackgroundColor = Panel.Color;
    }

}
