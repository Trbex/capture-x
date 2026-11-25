using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RealCaptureX;

internal sealed class ToolbarWindow : Window
{
    private readonly TextBlock status = Appearance.Label("", 11, Appearance.Muted);
    private readonly Button[] captureButtons = new Button[3];
    private string defaultStatus = "";

    public ToolbarWindow(Action<CaptureCommand> execute, Action hide)
    {
        Title = "REAL Capture X";
        NativeWindow.ToolWindow(this);
        var root = new StackPanel
        {
            Background = Appearance.Surface, Padding = new Thickness(12, 9, 12, 9), Spacing = 6,
            RequestedTheme = ElementTheme.Dark
        };
        var header = new Grid { ColumnSpacing = 7 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = Appearance.Icon("\uE722", 14);
        mark.Foreground = Appearance.Accent;
        var title = Appearance.Label("REAL Capture X", 14);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        Grid.SetColumn(title, 1);
        var close = Appearance.IconButton("\uE711", "Hide toolbar", hide);
        Grid.SetColumn(close, 2);
        header.Children.Add(mark);
        header.Children.Add(title);
        header.Children.Add(close);
        root.Children.Add(header);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        captureButtons[0] = CaptureButton("\uE003", "Region", CaptureCommand.Region);
        captureButtons[1] = CaptureButton("\uE737", "Window", CaptureCommand.Window);
        captureButtons[2] = CaptureButton("\uE7F4", "Display", CaptureCommand.Display);
        foreach (var captureButton in captureButtons) actions.Children.Add(captureButton);
        actions.Children.Add(Appearance.Divider());
        actions.Children.Add(Appearance.IconButton("\uE81C", "Capture history", () => execute(CaptureCommand.History)));
        actions.Children.Add(Appearance.IconButton("\uE713", "Settings", () => execute(CaptureCommand.Settings)));
        root.Children.Add(actions);
        var more = Appearance.IconButton("\uE712", "More capture modes");
        var modes = new MenuFlyout();
        var textMode = new MenuFlyoutItem { Text = "Capture text (OCR)" };
        textMode.Click += (_, _) => execute(CaptureCommand.Text);
        modes.Items.Add(textMode);
        var scrollMode = new MenuFlyoutItem { Text = "Scrolling capture" };
        scrollMode.Click += (_, _) => execute(CaptureCommand.Scroll);
        modes.Items.Add(scrollMode);
        AddMode("Horizontal scrolling capture", CaptureCommand.ScrollHorizontal);
        AddMode("Record region", CaptureCommand.RecordRegion);
        AddMode("Record display", CaptureCommand.RecordDisplay);
        AddMode("Open video in Studio", CaptureCommand.OpenStudio);
        AddMode("Repeat last region", CaptureCommand.RepeatRegion);
        more.Flyout = modes;
        actions.Children.Add(more);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(status,
            Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        ToolTipService.SetToolTip(status, status.Text);
        root.Children.Add(status);
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) hide();
        };
        Content = root;

        void AddMode(string label, CaptureCommand command)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => execute(command);
            modes.Items.Add(item);
        }

        Button CaptureButton(string glyph, string title, CaptureCommand command)
        {
            var label = Appearance.Label(title, 12);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            row.Children.Add(Appearance.Icon(glyph));
            row.Children.Add(label);
            var button = new Button
            {
                Content = row, Background = Appearance.Panel, Foreground = Appearance.Text, Height = 34,
                Padding = new Thickness(10, 0, 10, 0), BorderBrush = Appearance.Line, CornerRadius = new CornerRadius(7)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Capture {title.ToLowerInvariant()}");
            button.Click += (_, _) => execute(command);
            return button;
        }
    }

    public void SetShortcuts(string[] bindings, IReadOnlyList<string>? unavailable)
    {
        for (var i = 0; i < captureButtons.Length; i++) ToolTipService.SetToolTip(captureButtons[i], bindings[i]);
        defaultStatus = unavailable is { Count: > 0 }
            ? $"Shortcut unavailable: {string.Join(", ", unavailable)}"
            : $"{bindings[0]}  ·  {bindings[1]}  ·  {bindings[2]}";
        SetStatus(null);
    }

    public void SetStatus(string? value)
    {
        status.Text = value ?? defaultStatus;
        ToolTipService.SetToolTip(status, status.Text);
        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(
            Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
