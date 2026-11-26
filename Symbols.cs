using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace RealCaptureX;

internal enum Glyph
{
    Mark, Region, Window, Display, Video, History, Settings, More, Close, Copy, Save, Check, Pin, Scan,
    Repeat, Scroll, HorizontalScroll, Studio, Power, Arrow, Box, Oval, Pen, Highlight, Redact, Text,
    Step, Crop, Undo, Redo, Background, Back, Next, Scale, Grip, Sun, Moon, System, Mic, Audio,
    Cursor, Zoom, Stop, Trash, Image, Play, Pause, Keyboard, ChevronDown, Gear, ClearFill,
    Pixelate, Plus, Minus, Fit, Select
}

internal static class Symbols
{
    internal static Viewbox Draw(Glyph glyph, double size = 18, Brush? ink = null)
    {
        var path = (Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{Data(glyph)}'/>");
        path.Stroke = ink ?? Appearance.Text;
        path.StrokeThickness = 1.65;
        path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round;
        path.StrokeLineJoin = PenLineJoin.Round;
        var drawing = new Canvas { Width = 24, Height = 24 };
        drawing.Children.Add(path);
        return new Viewbox
        {
            Width = size, Height = size, Child = drawing, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static string Data(Glyph glyph) => glyph switch
    {
        Glyph.Mark => "M4 9 V4 H9 M15 4 H20 V9 M20 15 V20 H15 M9 20 H4 V15 M9 15 L15 9 M11.5 9 H15 V12.5",
        Glyph.Region => "M4 9 V4 H9 M15 4 H20 V9 M20 15 V20 H15 M9 20 H4 V15 M12 10.2 V13.8 M10.2 12 H13.8",
        Glyph.Window => "M4 5 H20 C21 5 21 6 21 7 V17 C21 18 20 19 19 19 H5 C4 19 3 18 3 17 V7 C3 6 3 5 4 5 Z M3 9 H21 M6 7 H6.5 M8.7 7 H9.2",
        Glyph.Display => "M4 4.5 H20 C21 4.5 21 5.5 21 6.5 V15 C21 16 20 16.5 19 16.5 H5 C4 16.5 3 16 3 15 V6.5 C3 5.5 3 4.5 4 4.5 Z M12 16.5 V20 M8 20 H16",
        Glyph.Video => "M4 6 H14 C15 6 16 7 16 8 V16 C16 17 15 18 14 18 H4 C3 18 2.5 17 2.5 16 V8 C2.5 7 3 6 4 6 Z M16 10 L21 7.5 V16.5 L16 14",
        Glyph.History => "M4.5 7 A8.5 8.5 0 1 1 3.5 14 M3 4 V9 H8 M12 7 V12 L15 14",
        Glyph.ClearFill => "M19.5 12 A7.5 7.5 0 1 0 4.5 12 A7.5 7.5 0 1 0 19.5 12 M6.7 17.3 L17.3 6.7",
        Glyph.Gear => "M10 3 H14 L14.5 5.5 L16 6.4 L18.5 5.6 L20.5 9 L18.7 10.8 V13.2 L20.5 15 L18.5 18.4 L16 17.6 L14.5 18.5 L14 21 H10 L9.5 18.5 L8 17.6 L5.5 18.4 L3.5 15 L5.3 13.2 V10.8 L3.5 9 L5.5 5.6 L8 6.4 L9.5 5.5 Z M15 12 A3 3 0 1 0 9 12 A3 3 0 1 0 15 12",
        Glyph.Settings => "M5 5 V19 M12 5 V19 M19 5 V19 M2.5 9 H7.5 M9.5 15 H14.5 M16.5 10 H21.5",
        Glyph.More => "M5.5 12 A.6 .6 0 1 0 4.3 12 A.6 .6 0 1 0 5.5 12 M12.6 12 A.6 .6 0 1 0 11.4 12 A.6 .6 0 1 0 12.6 12 M19.7 12 A.6 .6 0 1 0 18.5 12 A.6 .6 0 1 0 19.7 12",
        Glyph.Close => "M6 6 L18 18 M18 6 L6 18",
        Glyph.Copy => "M9 8 H19 Q21 8 21 10 V19 Q21 21 19 21 H10 Q8 21 8 19 V10 Q8 8 9 8 Z M16 5 Q16 3 14 3 H5 Q3 3 3 5 V14 Q3 16 5 16",
        Glyph.Save => "M12 3 V14 M8 10 L12 14 L16 10 M4.5 15 V19 Q4.5 20.5 6 20.5 H18 Q19.5 20.5 19.5 19 V15",
        Glyph.Check => "M5 12 L9.5 16.5 L19 7",
        Glyph.Pin => "M13 3 L21 11 M15 5 L10 9 L6.5 9.5 L5 11 L13 19 L14.5 17.5 L15 14 L19 9 M9 15 L4 20",
        Glyph.Scan => "M3 8 V4 H7 M17 4 H21 V8 M21 16 V20 H17 M7 20 H3 V16 M7 9 H17 M7 12 H17 M7 15 H13",
        Glyph.Repeat => "M20 5 V10 H15 M4 19 V14 H9 M4 10 A8 8 0 0 1 18 6 L20 10 M20 14 A8 8 0 0 1 6 18 L4 14",
        Glyph.Scroll => "M8 3 H16 Q18 3 18 5 V19 Q18 21 16 21 H8 Q6 21 6 19 V5 Q6 3 8 3 Z M12 7 V17 M9.7 9.3 L12 7 L14.3 9.3 M9.7 14.7 L12 17 L14.3 14.7",
        Glyph.HorizontalScroll => "M3 8 Q3 6 5 6 H19 Q21 6 21 8 V16 Q21 18 19 18 H5 Q3 18 3 16 Z M7 12 H17 M9.3 9.7 L7 12 L9.3 14.3 M14.7 9.7 L17 12 L14.7 14.3",
        Glyph.Studio => "M4 4 H20 Q21 4 21 5 V19 Q21 20 20 20 H4 Q3 20 3 19 V5 Q3 4 4 4 Z M3 15 H21 M9 7 L15 10 L9 13 Z",
        Glyph.Power => "M12 3 V12 M6.5 6.2 A8 8 0 1 0 17.5 6.2",
        Glyph.Arrow => "M5 19 L18.5 5.5 M10.5 5.5 H18.5 V13.5",
        Glyph.Box => "M5 5 H19 Q20 5 20 6 V18 Q20 19 19 19 H5 Q4 19 4 18 V6 Q4 5 5 5 Z",
        Glyph.Oval => "M20.5 12 A8.5 7 0 1 0 3.5 12 A8.5 7 0 1 0 20.5 12",
        Glyph.Pen => "M4 20 L8.6 19 L20 7.5 Q21.3 6.2 20 4.9 L19.1 4 Q17.8 2.7 16.5 4 L5 15.4 Z M14.3 6.2 L17.8 9.7",
        Glyph.Highlight => "M8 13 L16.5 4.5 Q17.5 3.5 18.5 4.5 L19.5 5.5 Q20.5 6.5 19.5 7.5 L11 16 Z M8 13 L5 16 L8 19 L11 16 M4 21 H14",
        Glyph.Redact => "M5 5 H19 Q20 5 20 6 V18 Q20 19 19 19 H5 Q4 19 4 18 V6 Q4 5 5 5 Z M7.5 9 H16.5 M7.5 12 H16.5 M7.5 15 H16.5",
        Glyph.Text => "M5 6 H19 M12 6 V19 M8.5 19 H15.5 M5 6 V9 M19 6 V9",
        Glyph.Step => "M21 12 A9 9 0 1 0 3 12 A9 9 0 1 0 21 12 M10 10 L12 8.5 V15.5 M10 15.5 H14",
        Glyph.Crop => "M7 3 V16 Q7 17 8 17 H21 M3 7 H16 Q17 7 17 8 V21",
        Glyph.Undo => "M8 7 L3.5 11 L8 15 M4 11 H15.5 A4 4 0 0 1 15.5 19 H12",
        Glyph.Redo => "M16 7 L20.5 11 L16 15 M20 11 H8.5 A4 4 0 0 0 8.5 19 H12",
        Glyph.Background => "M4 5 H17 Q19 5 19 7 V17 Q19 19 17 19 H4 Q3 19 3 17 V7 Q3 5 4 5 Z M7 9 H15 V15 H7 Z M21 8 V20",
        Glyph.Back => "M14.5 5.5 L8 12 L14.5 18.5",
        Glyph.Next => "M9.5 5.5 L16 12 L9.5 18.5",
        Glyph.Scale => "M14 4 H20 V10 M20 4 L14 10 M10 20 H4 V14 M4 20 L10 14",
        Glyph.Grip => "M6 9 H18 M6 15 H18",
        Glyph.Sun => "M16 12 A4 4 0 1 0 8 12 A4 4 0 1 0 16 12 M12 2.5 V5 M12 19 V21.5 M2.5 12 H5 M19 12 H21.5 M5.3 5.3 L7 7 M17 17 L18.7 18.7 M5.3 18.7 L7 17 M17 7 L18.7 5.3",
        Glyph.Moon => "M20.5 15 A8.7 8.7 0 1 1 9 3.5 A8 8 0 0 0 20.5 15 Z",
        Glyph.System => "M4 4 H20 Q21 4 21 6 V16 Q21 18 19 18 H5 Q3 18 3 16 V6 Q3 4 4 4 Z M5.5 14.5 L14 6 M10 16 L18 8 M12 18 V21",
        Glyph.Mic => "M9 6 A3 3 0 0 1 15 6 V12 A3 3 0 0 1 9 12 Z M6 11 V12 A6 6 0 0 0 18 12 V11 M12 18 V21",
        Glyph.Audio => "M3.5 9 H7 L11 5.5 V18.5 L7 15 H3.5 Z M15 8.5 A5 5 0 0 1 15 15.5 M18 5.5 A9 9 0 0 1 18 18.5",
        Glyph.Cursor => "M5 3.5 L19 13 L13 14 L10 20 Z",
        Glyph.Zoom => "M18 10.5 A7 7 0 1 0 4 10.5 A7 7 0 1 0 18 10.5 M16 16 L21 21 M8 10.5 H14 M11 7.5 V13.5",
        Glyph.Stop => "M7 6 H17 Q18 6 18 7 V17 Q18 18 17 18 H7 Q6 18 6 17 V7 Q6 6 7 6 Z",
        Glyph.Trash => "M4.5 7 H19.5 M9 7 V4 H15 V7 M6.5 7 L7.5 20 H16.5 L17.5 7 M10.5 10.5 V16.5 M13.5 10.5 V16.5",
        Glyph.Image => "M5 4.5 H19 Q21 4.5 21 6 V18 Q21 19.5 19 19.5 H5 Q3 19.5 3 18 V6 Q3 4.5 5 4.5 Z M3 16 L8.5 11 L13 15 L16 12 L21 16.5 M16.8 8.4 A1 1 0 1 0 14.8 8.4 A1 1 0 1 0 16.8 8.4",
        Glyph.Play => "M8 4.5 L19 12 L8 19.5 Z",
        Glyph.Pause => "M9 5.5 V18.5 M15 5.5 V18.5",
        Glyph.Keyboard => "M4 6 H20 Q21 6 21 8 V16 Q21 18 20 18 H4 Q3 18 3 16 V8 Q3 6 4 6 Z M7 10 H7.2 M10.5 10 H10.7 M14 10 H14.2 M17.5 10 H17.7 M7 14 H17",
        Glyph.ChevronDown => "M6 9 L12 15 L18 9",
        Glyph.Pixelate => "M4 4 H9 V9 H4 Z M15 4 H20 V9 H15 Z M9.5 9.5 H14.5 V14.5 H9.5 Z M4 15 H9 V20 H4 Z M15 15 H20 V20 H15 Z",
        Glyph.Plus => "M12 5 V19 M5 12 H19",
        Glyph.Minus => "M5 12 H19",
        Glyph.Fit => "M4 9 V4 H9 M15 4 H20 V9 M20 15 V20 H15 M9 20 H4 V15 M9 9 H15 V15 H9 Z",
        Glyph.Select => "M6 3.5 L18.5 12.2 L12.6 13.4 L9.6 19.2 Z",
        _ => throw new ArgumentOutOfRangeException(nameof(glyph))
    };
}
