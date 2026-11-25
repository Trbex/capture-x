using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static RealCaptureX.CaptureMenu;

namespace RealCaptureX;

internal sealed class ToolbarWindow : Window
{
    internal const int DockWidth = 501;
    internal const int DockHeight = 106;

    private readonly Border frame;
    private readonly TextBlock status = Appearance.Label("", 11, Appearance.Muted);
    private readonly Button[] captureButtons = new Button[3];
    private readonly string[] labels = ["Capture region", "Capture window", "Capture display"];
    private string? defaultStatus;
    private int revision;

    public ToolbarWindow(Action<CaptureCommand> execute, Action hide, Action<NativeWindow.Rect> context)
    {
        Title = "REAL Capture X";
        NativeWindow.ToolWindow(this);
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        shell.RowDefinitions.Add(new RowDefinition());
        frame = Appearance.Frame(shell);

        var header = new Grid { Padding = new Thickness(8, 2, 5, 2), ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        brand.Children.Add(Appearance.Mark(24));
        brand.Children.Add(Appearance.Label("REAL Capture X", 12, strong: true));
        header.Children.Add(brand);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetColumn(status, 1);
        header.Children.Add(status);

        var utility = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        utility.Children.Add(Appearance.IconButton(Glyph.History, "Capture history", () => execute(CaptureCommand.History), 27));
        utility.Children.Add(Appearance.IconButton(Glyph.Settings, "Settings", () => execute(CaptureCommand.Settings), 27));
        var more = Appearance.IconButton(Glyph.More, "More capture modes", size: 27);
        more.Flyout = CaptureMenu.Flyout(
            new("Capture",
                Mode(CaptureCommand.RepeatRegion, "Repeat last region", Glyph.Repeat, execute),
                Mode(CaptureCommand.ScrollHorizontal, "Horizontal scrolling", Glyph.HorizontalScroll, execute),
                Mode(CaptureCommand.Text, "Capture text (OCR)", Glyph.Scan, execute)),
            new("Workspace",
                Mode(CaptureCommand.Recent, "Quick Access", Glyph.Image, execute),
                Mode(CaptureCommand.OpenStudio, "Open video in Studio", Glyph.Studio, execute)));
        utility.Children.Add(more);
        utility.Children.Add(Appearance.IconButton(Glyph.Close, "Hide toolbar", hide, 27));
        Grid.SetColumn(utility, 2);
        header.Children.Add(utility);
        shell.Children.Add(header);
        var rule = Appearance.Rule();
        Grid.SetRow(rule, 1);
        shell.Children.Add(rule);

        var modes = new Grid { ColumnSpacing = 3, Padding = new Thickness(6, 5, 6, 6) };
        for (var i = 0; i < 7; i++)
            modes.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(13) : new GridLength(1, GridUnitType.Star) });
        captureButtons[0] = Tile("Region", Glyph.Region, CaptureCommand.Region);
        captureButtons[1] = Tile("Window", Glyph.Window, CaptureCommand.Window);
        captureButtons[2] = Tile("Display", Glyph.Display, CaptureCommand.Display);
        Appearance.SetTone(captureButtons[0], ButtonTone.Selected);
        for (var i = 0; i < captureButtons.Length; i++) AddTile(captureButtons[i], i);
        var divider = Appearance.Divider();
        divider.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(divider, 3);
        modes.Children.Add(divider);
        AddTile(Tile("Scroll", Glyph.Scroll, CaptureCommand.Scroll), 4);
        AddTile(Tile("Text", Glyph.Scan, CaptureCommand.Text), 5);
        var record = Tile("Record", Glyph.Video);
        record.Flyout = CaptureMenu.Flyout(new MenuSection("Recording",
            Mode(CaptureCommand.RecordRegion, "Record region", Glyph.Region, execute),
            Mode(CaptureCommand.RecordDisplay, "Record display", Glyph.Display, execute)));
        AddTile(record, 6);
        Grid.SetRow(modes, 2);
        shell.Children.Add(modes);
        frame.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            hide();
        };
        frame.RightTapped += (_, e) =>
        {
            var position = AppWindow.Position;
            var size = AppWindow.Size;
            context(new NativeWindow.Rect
            {
                Left = position.X, Right = position.X + size.Width, Top = position.Y, Bottom = position.Y + size.Height
            });
            e.Handled = true;
        };
        Appearance.SetContent(this, frame, floating: true);

        Button Tile(string label, Glyph glyph, CaptureCommand? command = null)
        {
            Action? start = command is { } mode ? () => execute(mode) : null;
            var button = Appearance.Action(label, start);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Stretch;
            button.Height = double.NaN;
            button.Padding = new Thickness(0);
            var contents = new StackPanel { Spacing = 4 };
            contents.Children.Add(Symbols.Draw(glyph, 21, glyph == Glyph.Region ? Appearance.AccentText : Appearance.Text));
            var text = Appearance.Label(label, 11, glyph == Glyph.Region ? Appearance.AccentText : Appearance.Text, strong: true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            contents.Children.Add(text);
            button.Content = contents;
            return button;
        }

        void AddTile(Button tile, int column)
        {
            Grid.SetColumn(tile, column);
            modes.Children.Add(tile);
        }
    }

    public void Show()
    {
        revision++;
        var visible = AppWindow.IsVisible;
        frame.IsHitTestVisible = true;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), DockWidth, DockHeight, .5, .86);
        ProductMotion.Enter(frame, !visible);
        Activate();
    }

    public void YieldToWorkspace()
    {
        if (AppWindow.IsVisible) ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = false;
    }

    public void HideNow()
    {
        revision++;
        AppWindow.Hide();
    }

    public async Task AnimateHideAsync()
    {
        if (!AppWindow.IsVisible) return;
        var current = ++revision;
        frame.IsHitTestVisible = false;
        await ProductMotion.ExitAsync(frame);
        if (current == revision) HideNow();
    }

    public void SetShortcuts(string[] bindings, IReadOnlyList<string>? unavailable)
    {
        for (var i = 0; i < captureButtons.Length; i++)
        {
            AutomationProperties.SetName(captureButtons[i], labels[i]);
            ToolTipService.SetToolTip(captureButtons[i], $"{labels[i]}  ·  {bindings[i]}");
        }
        defaultStatus = unavailable is { Count: > 0 } ? $"Shortcut unavailable: {string.Join(", ", unavailable)}" : null;
        SetStatus(null);
    }

    public void SetStatus(string? value)
    {
        status.Text = value ?? defaultStatus ?? "";
        ToolTipService.SetToolTip(status, status.Text);
        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(
            Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
