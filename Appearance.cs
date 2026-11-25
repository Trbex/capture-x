using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RealCaptureX;

internal static class Appearance
{
    internal static readonly SolidColorBrush Surface = Brush(26, 28, 33);
    internal static readonly SolidColorBrush Panel = Brush(35, 38, 45);
    internal static readonly SolidColorBrush Text = Brush(248, 249, 253);
    internal static readonly SolidColorBrush Muted = Brush(178, 186, 201);
    internal static readonly SolidColorBrush Accent = Brush(255, 169, 114);
    internal static readonly SolidColorBrush Line = Brush(58, 63, 74);

    internal static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) =>
        new(Color.FromArgb(a, r, g, b));

    internal static TextBlock Label(string value, double size = 12, Brush? brush = null) =>
        new() { Text = value, FontSize = size, Foreground = brush ?? Text, VerticalAlignment = VerticalAlignment.Center };

    internal static FontIcon Icon(string glyph, double size = 15) =>
        new() { Glyph = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons") };

    internal static Button IconButton(string glyph, string name, Action? execute = null)
    {
        var button = new Button
        {
            Content = Icon(glyph), Width = 31, Height = 30, Padding = new Thickness(0),
            Background = Brush(0, 0, 0, 0), BorderThickness = new Thickness(0),
            Foreground = Text, CornerRadius = new CornerRadius(6)
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        if (execute is not null) button.Click += (_, _) => execute();
        return button;
    }

    internal static Border Divider() =>
        new() { Width = 1, Height = 20, Background = Line, Margin = new Thickness(2, 0, 2, 0) };

    internal static void StyleTitleBar(Window window)
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
