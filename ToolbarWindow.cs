using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Numerics;
using Windows.System;
using static RealCaptureX.CaptureMenu;

namespace RealCaptureX;

internal sealed class ToolbarWindow : Window
{
    internal const int PanelWidth = 612;
    internal const int PanelHeight = 134;

    private readonly GlassSurface glass;
    private readonly TextBlock status = Appearance.Label("", 11, Appearance.Muted);
    private readonly Button[] captureButtons = new Button[3];
    private readonly string[] labels = ["Capture region", "Capture window", "Capture display"];
    private readonly List<Button> tiles = [];
    private readonly Border indicator;
    private readonly Grid modes;
    private readonly Button pin;
    private Button selected = null!;
    private bool pinned;
    private string? defaultStatus;
    private int revision;

    public ToolbarWindow(Action<CaptureCommand> execute, Action hide, Action<NativeWindow.Rect, bool> context)
    {
        Title = "REAL Capture X";
        NativeWindow.ToolWindow(this);
        NativeWindow.ExcludeOverlayFromCapture(this);
        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        shell.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Padding = new Thickness(14, 0, 10, 0), ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 11 };
        brand.Children.Add(Appearance.Mark(30));
        brand.Children.Add(Appearance.Label("REAL Capture X", 15, strong: true));
        header.Children.Add(brand);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.HorizontalAlignment = HorizontalAlignment.Right;
        status.Margin = new Thickness(0, 0, 6, 0);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetColumn(status, 1);
        header.Children.Add(status);

        var utility = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        pin = Appearance.IconButton(Glyph.Pin, "Keep toolbar above workspace", TogglePin, 32);
        utility.Children.Add(pin);
        utility.Children.Add(Appearance.IconButton(Glyph.History, "Capture history", () => execute(CaptureCommand.History), 32));
        var split = Appearance.Divider();
        split.Height = 16;
        utility.Children.Add(split);
        utility.Children.Add(Appearance.IconButton(Glyph.Settings, "Settings", () => execute(CaptureCommand.Settings), 32));
        Button more = null!;
        more = Appearance.IconButton(Glyph.More, "Quick menu", () => context(NativeWindow.ScreenRect(this, more), true), 32);
        utility.Children.Add(more);
        utility.Children.Add(Appearance.IconButton(Glyph.Close, "Hide toolbar", hide, 32));
        Grid.SetColumn(utility, 2);
        header.Children.Add(utility);
        shell.Children.Add(header);
        var rule = Appearance.Rule();
        rule.Margin = new Thickness(1, 0, 1, 0);
        Grid.SetRow(rule, 1);
        shell.Children.Add(rule);

        modes = new Grid { Padding = new Thickness(10, 8, 10, 10) };
        for (var i = 0; i < 7; i++)
            modes.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == 3 ? new GridLength(13) : new GridLength(1, GridUnitType.Star)
            });
        indicator = new Border
        {
            CornerRadius = new CornerRadius(12), Background = Appearance.SelectedFill, BorderBrush = Appearance.SelectedRim,
            BorderThickness = new Thickness(1), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumnSpan(indicator, 7);
        modes.Children.Add(indicator);

        captureButtons[0] = Tile("Region", Glyph.Region, CaptureCommand.Region);
        captureButtons[1] = Tile("Window", Glyph.Window, CaptureCommand.Window);
        captureButtons[2] = Tile("Display", Glyph.Display, CaptureCommand.Display);
        for (var i = 0; i < captureButtons.Length; i++) AddTile(captureButtons[i], i);
        var divider = Appearance.Divider();
        divider.Height = 34;
        divider.Margin = new Thickness(0);
        divider.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(divider, 3);
        modes.Children.Add(divider);
        AddTile(Tile("Scroll", Glyph.Scroll, CaptureCommand.Scroll), 4);
        AddTile(Tile("Text", Glyph.Scan, CaptureCommand.Text), 5);
        var record = Tile("Record", Glyph.Video);
        record.Click += (_, _) =>
        {
            // The tile that owns the open menu keeps the highlight until the menu closes: tile and menu read as one object.
            // (Set after ShowMenu: opening a menu first runs the previous menu's close callback.)
            TrayMenuWindow.Shared?.ShowMenu(NativeWindow.ScreenRect(this, record), null,
                [new MenuSection("Record",
                    Mode(CaptureCommand.RecordRegion, "Record region", Glyph.Region, execute),
                    Mode(CaptureCommand.RecordDisplay, "Record display", Glyph.Display, execute))], 236,
                onClosed: () =>
                {
                    menuOwner = null;
                    Slide(selected);
                });
            menuOwner = record;
            Slide(record);
        };
        AddTile(record, 6);
        selected = captureButtons[0];
        modes.PointerExited += (_, _) => Slide(menuOwner ?? selected);
        modes.SizeChanged += (_, _) => Slide(selected, animate: false);
        Grid.SetRow(modes, 2);
        shell.Children.Add(modes);

        glass = Appearance.Float(this, shell, radius: 16);
        glass.Panel.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            e.Handled = true;
            hide();
        };
        // A right click opens the full menu at the pointer, growing out of the point that was clicked.
        glass.Panel.RightTapped += (_, e) =>
        {
            var point = e.GetPosition(null);
            var scale = Scale();
            var x = AppWindow.Position.X + (int)Math.Round(point.X * scale);
            var y = AppWindow.Position.Y + (int)Math.Round(point.Y * scale);
            context(new NativeWindow.Rect { Left = x, Right = x, Top = y, Bottom = y }, false);
            e.Handled = true;
        };

        Button Tile(string label, Glyph glyph, CaptureCommand? command = null)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["TileButton"], Background = Appearance.Clear,
                BorderThickness = new Thickness(0), Foreground = Appearance.Text,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(12)
            };
            AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            var contents = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            contents.Children.Add(Symbols.Draw(glyph, 26));
            var text = Appearance.Label(label, 13);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            contents.Children.Add(text);
            button.Content = contents;
            if (command is { } mode)
                button.Click += (_, _) =>
                {
                    selected = button;
                    Slide(button, animate: false);
                    execute(mode);
                };
            button.PointerEntered += (_, _) => Slide(button);
            // The shared pill is the physical object under the finger: it gives a little on press (pointer-down,
            // not click) and springs back on release, cancel or leaving the tile.
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Press(button, true)), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Press(button, false)), true);
            button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => Press(button, false)), true);
            button.PointerExited += (_, _) => Press(button, false);
            button.GotFocus += (_, _) => Slide(button);
            button.LostFocus += (_, _) => Slide(menuOwner ?? selected);
            return button;
        }

        void AddTile(Button tile, int column)
        {
            Grid.SetColumn(tile, column);
            tiles.Add(tile);
            modes.Children.Add(tile);
        }
    }

    private double Scale() => Math.Max(96, NativeWindow.GetDpiForWindow(NativeWindow.Handle(this))) / 96.0;

    private Button? pressed;
    private Button? menuOwner;

    private void Press(Button tile, bool down)
    {
        if (down) pressed = tile;
        else if (!ReferenceEquals(pressed, tile)) return;
        else pressed = null;
        var scale = down ? .955f : 1f;
        ProductMotion.ScaleTo(indicator, new Vector2(scale, scale), spec: SpringSpec.Press);
    }

    /// <summary>Moves the shared highlight pill to a tile; a spring keeps hover → hover travel fluid.</summary>
    private void Slide(Button tile, bool animate = true)
    {
        if (tile.ActualWidth <= 0) return;
        indicator.Width = tile.ActualWidth;
        var offset = tile.TransformToVisual(modes).TransformPoint(new Windows.Foundation.Point(0, 0));
        ProductMotion.MoveTo(indicator, new Vector2((float)offset.X - (float)modes.Padding.Left, 0), animate);
    }

    private void TogglePin()
    {
        pinned = !pinned;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = pinned;
        Appearance.SetTone(pin, pinned ? ButtonTone.Selected : ButtonTone.Quiet);
        AutomationProperties.SetItemStatus(pin, pinned ? "Pinned" : "");
        ToolTipService.SetToolTip(pin, pinned ? "Release toolbar from top" : "Keep toolbar above workspace");
    }

    public void Show()
    {
        revision++;
        var visible = AppWindow.IsVisible;
        glass.Panel.IsHitTestVisible = true;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), GlassSurface.Outer(PanelWidth),
            GlassSurface.Outer(PanelHeight), .5, .88, GlassSurface.Margin);
        Activate();
        Slide(selected, animate: false);
        // Rises out of the bottom of the screen area it sits above and sinks back the same way when hidden.
        if (!visible) glass.Enter(rise: 10, origin: new Vector2(.5f, 1));
        else glass.Settle();
    }

    public void YieldToWorkspace()
    {
        if (!pinned && AppWindow.IsVisible) ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = false;
    }

    public void HideNow()
    {
        revision++;
        AppWindow.Hide();
        glass.Conceal();
    }

    public async Task AnimateHideAsync()
    {
        if (!AppWindow.IsVisible) return;
        var current = ++revision;
        glass.Panel.IsHitTestVisible = false;
        await glass.ExitAsync(drop: 6);
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
