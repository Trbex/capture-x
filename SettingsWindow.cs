using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;

namespace RealCaptureX;

internal sealed class SettingsWindow : Window
{
    private readonly TextBlock status = Appearance.Label("", 12, Appearance.Muted);
    private readonly Grid page = new();
    private readonly Grid rail = new() { Padding = new Thickness(12, 14, 12, 12) };
    private readonly StackPanel railItems = new() { Spacing = 3 };
    private readonly Border railIndicator;
    private readonly List<(Button Button, FrameworkElement Content)> categories = [];
    private readonly List<(Button Card, Border Check, ProductTheme Theme)> themeChoices = [];
    private Button? activeCategory;
    private ProductTheme theme;
    private ProductTheme savedTheme;
    private bool translucency;
    private bool savedTranslucency;
    private bool reduceMotion;
    private bool savedReduceMotion;

    public SettingsWindow(CaptureSettings initial, Action<CaptureSettings> save)
    {
        Title = "REAL Capture X — Settings";
        theme = savedTheme = initial.Theme;
        translucency = savedTranslucency = initial.Translucency;
        reduceMotion = savedReduceMotion = initial.ReduceMotion;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        railIndicator = new Border
        {
            Height = 40, CornerRadius = new CornerRadius(10), Background = Appearance.SelectedFill,
            BorderBrush = Appearance.SelectedRim, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        rail.Children.Add(railIndicator);
        rail.Children.Add(railItems);
        workspace.Children.Add(rail);
        var split = new Border { Background = Appearance.Line };
        Grid.SetColumn(split, 1);
        workspace.Children.Add(split);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Grid.SetColumn(scroll, 2);
        workspace.Children.Add(scroll);
        root.Children.Add(workspace);
        var rule = Appearance.Rule();
        Grid.SetRow(rule, 1);
        root.Children.Add(rule);

        var cursor = Switch("Include pointer in screenshots", initial.IncludePointer);
        var showPreview = Switch("Show Quick Access after capture", initial.ShowPreview);
        var autoCopy = Switch("Copy screenshots automatically", initial.AutoCopy);
        var translucent = Switch("Window translucency", translucency);
        translucent.Toggled += (_, _) => { translucency = translucent.IsOn; Preview(); };
        var motion = Switch("Reduce motion", reduceMotion);
        motion.Toggled += (_, _) => { reduceMotion = motion.IsOn; Preview(); };
        var corner = new ComboBox
        {
            ItemsSource = new[] { "Bottom left", "Bottom right", "Top left", "Top right" },
            SelectedIndex = initial.Corner, MinWidth = 150
        };
        AutomationProperties.SetName(corner, "Preview position");
        var timeout = new NumberBox
        {
            Value = initial.Timeout, Minimum = 0, Maximum = 600, Width = 120,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        AutomationProperties.SetName(timeout, "Hide after seconds");
        var shortcuts = new[] { "Capture region", "Capture window", "Capture display", "Open toolbar", "Restore Quick Access" };
        var keys = shortcuts.Select((label, index) =>
        {
            var field = new TextBox { Text = initial.KeyBindings[index], MaxLength = 32, Width = 170 };
            AutomationProperties.SetName(field, label);
            return field;
        }).ToArray();

        var appearance = Page("Appearance", "Customize the look and feel of REAL Capture X.");
        appearance.Children.Add(Appearance.Label("Theme", 14, strong: true));
        var swatches = new Grid { ColumnSpacing = 12 };
        foreach (var choice in Enum.GetValues<ProductTheme>())
        {
            swatches.ColumnDefinitions.Add(new ColumnDefinition());
            var card = ThemeCard(choice);
            Grid.SetColumn(card.Card, (int)choice);
            swatches.Children.Add(card.Card);
            themeChoices.Add(card);
        }
        appearance.Children.Add(swatches);
        appearance.Children.Add(Group(
            Row("Window translucency", "Use a subtle blur and transparency on supported systems.", translucent),
            Row("Reduce motion", "Replace movement with gentle fades. Windows animation settings are always respected.", motion)));
        UpdateThemeChoices();

        var capture = Page("Capture", "How screenshots are taken.");
        capture.Children.Add(Group(Row("Include pointer in screenshots", "Adds the mouse pointer to region, window and display captures.", cursor)));

        var quick = Page("Quick Access", "The floating preview shown after every capture.");
        quick.Children.Add(Group(
            Row("Show after capture", null, showPreview),
            Row("Copy automatically", "Every new screenshot also goes to the clipboard.", autoCopy),
            Row("Position", null, corner),
            Row("Hide after seconds", "Set to 0 to keep previews visible.", timeout)));

        var keyboard = Page("Shortcuts", "Use Ctrl or Alt with a letter, number, or function key.");
        keyboard.Children.Add(Group(shortcuts.Select((label, index) => Row(label, null, keys[index])).ToArray()));

        var about = Page("About", "Native screen capture and recording for Windows 11.");
        var version = typeof(App).Assembly.GetName().Version;
        about.Children.Add(Group(
            Row("Version", null, Appearance.Label($"{version?.Major}.{version?.Minor}.{version?.Build}", 13, Appearance.Muted)),
            Row("Captures and recordings", CaptureStore.Root, Appearance.Action("Open folder", () =>
            {
                Directory.CreateDirectory(CaptureStore.Root);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{CaptureStore.Root}\"") { UseShellExecute = true });
            }, Glyph.Image, ButtonTone.Raised))));

        AddCategory("Appearance", Glyph.Image, appearance);
        AddCategory("Capture", Glyph.Region, capture);
        AddCategory("Quick Access", Glyph.Copy, quick);
        AddCategory("Shortcuts", Glyph.Keyboard, keyboard);
        AddCategory("About", Glyph.Mark, about);
        Select(categories[0].Button, animate: false);

        var footer = new Grid { Padding = new Thickness(24, 0, 20, 0), ColumnSpacing = 10 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        status.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        footer.Children.Add(status);
        var apply = Appearance.Action("Save Settings", () =>
        {
            try
            {
                save(initial with
                {
                    Corner = Math.Max(0, corner.SelectedIndex),
                    Timeout = double.IsFinite(timeout.Value) ? (int)Math.Clamp(timeout.Value, 0, 600) : initial.Timeout,
                    ShowPreview = showPreview.IsOn,
                    AutoCopy = autoCopy.IsOn,
                    IncludePointer = cursor.IsOn,
                    Shortcuts = keys.Select(key => key.Text.Trim()).ToArray(),
                    Theme = theme,
                    Translucency = translucency,
                    ReduceMotion = reduceMotion
                });
                savedTheme = theme;
                savedTranslucency = translucency;
                savedReduceMotion = reduceMotion;
                status.Text = "";
                ToastWindow.Show("Settings saved", "Your preferences are active.");
            }
            catch (Exception ex) { status.Text = ex.Message; }
            ToolTipService.SetToolTip(status, status.Text);
        }, Glyph.Check, ButtonTone.Primary);
        var reset = Appearance.Action("Reset to default", () =>
        {
            var defaults = new CaptureSettings();
            cursor.IsOn = defaults.IncludePointer;
            showPreview.IsOn = defaults.ShowPreview;
            autoCopy.IsOn = defaults.AutoCopy;
            corner.SelectedIndex = defaults.Corner;
            timeout.Value = defaults.Timeout;
            for (var i = 0; i < keys.Length; i++) keys[i].Text = defaults.KeyBindings[i];
            translucent.IsOn = defaults.Translucency;
            motion.IsOn = defaults.ReduceMotion;
            theme = defaults.Theme;
            Preview();
            UpdateThemeChoices();
            status.Text = "Defaults restored. Save to keep them.";
        }, tone: ButtonTone.Raised);
        foreach (var button in new[] { reset, apply })
        {
            button.Height = 40;
            button.CornerRadius = new CornerRadius(10);
            button.Padding = new Thickness(18, 0, 18, 0);
        }
        apply.MinWidth = 150;
        Grid.SetColumn(reset, 1);
        Grid.SetColumn(apply, 2);
        footer.Children.Add(reset);
        footer.Children.Add(apply);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Appearance.SetContent(this, root, caption: "Settings");
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 900, 640, .5, .47);
        root.Loaded += (_, _) => MoveIndicator(categories[0].Button, animate: false);
        Closed += (_, _) =>
        {
            if (savedTheme != theme || savedTranslucency != translucency || savedReduceMotion != reduceMotion)
            {
                Appearance.SetPreferences(savedTranslucency, savedReduceMotion);
                Appearance.SetTheme(savedTheme);
            }
        };
    }

    /// <summary>Live preview of appearance choices; Cancel/close restores the saved state.</summary>
    private void Preview()
    {
        status.Text = "";
        Appearance.SetPreferences(translucency, reduceMotion);
        Appearance.SetTheme(theme);
    }

    private static StackPanel Page(string title, string subtitle)
    {
        var content = new StackPanel { Padding = new Thickness(32, 26, 30, 30), Spacing = 18 };
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(Appearance.Label(title, 28, strong: true));
        heading.Children.Add(Appearance.Label(subtitle, 13, Appearance.Muted));
        content.Children.Add(heading);
        return content;
    }

    private void AddCategory(string label, Glyph icon, FrameworkElement content)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["CaptureButton"], Background = Appearance.Clear,
            BorderThickness = new Thickness(0), Foreground = Appearance.Text, Height = 40, CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 0, 12, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(Symbols.Draw(icon, 18));
        row.Children.Add(Appearance.Label(label, 13));
        button.Content = row;
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => Select(button, animate: true);
        categories.Add((button, content));
        railItems.Children.Add(button);
    }

    private void Select(Button target, bool animate)
    {
        if (ReferenceEquals(activeCategory, target)) return;
        var previous = categories.FindIndex(c => ReferenceEquals(c.Button, activeCategory));
        var next = categories.FindIndex(c => ReferenceEquals(c.Button, target));
        activeCategory = target;
        foreach (var (button, _) in categories)
            AutomationProperties.SetItemStatus(button, ReferenceEquals(button, target) ? "Selected" : "");
        MoveIndicator(target, animate);
        page.Children.Clear();
        var content = categories[next].Content;
        page.Children.Add(content);
        if (animate) ProductMotion.Reveal(content, slide: next > previous ? 14 : -14);
    }

    private void MoveIndicator(Button target, bool animate)
    {
        if (target.ActualHeight <= 0) return;
        var offset = target.TransformToVisual(railItems).TransformPoint(new Windows.Foundation.Point(0, 0));
        ProductMotion.MoveTo(railIndicator, new Vector2(0, (float)offset.Y), animate);
    }

    private (Button Card, Border Check, ProductTheme Theme) ThemeCard(ProductTheme choice)
    {
        var label = choice.ToString();
        var button = Appearance.Action(label, () =>
        {
            if (theme == choice) return;
            theme = choice;
            Preview();
            UpdateThemeChoices();
        });
        button.Padding = new Thickness(10, 10, 10, 10);
        button.CornerRadius = new CornerRadius(14);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Height = 136;
        var panel = new Grid { RowSpacing = 10 };
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var miniature = new Grid { CornerRadius = new CornerRadius(8) };
        if (choice == ProductTheme.System)
        {
            miniature.ColumnDefinitions.Add(new ColumnDefinition());
            miniature.ColumnDefinitions.Add(new ColumnDefinition());
            miniature.Children.Add(Sample(true, left: true));
            var dark = Sample(false, left: false);
            Grid.SetColumn(dark, 1);
            miniature.Children.Add(dark);
        }
        else miniature.Children.Add(Sample(choice == ProductTheme.Light, left: null));
        panel.Children.Add(miniature);
        var caption = new Grid();
        caption.Children.Add(Appearance.Label(label, 13));
        var check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Background = Appearance.Accent, Child = Symbols.Draw(Glyph.Check, 13, Appearance.OnAccent),
            HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false
        };
        caption.Children.Add(check);
        Grid.SetRow(caption, 1);
        panel.Children.Add(caption);
        button.Content = panel;
        return (button, check, choice);
    }

    /// <summary>Miniature window: traffic dots, a sidebar and two content lines.</summary>
    private static Border Sample(bool light, bool? left)
    {
        var ink = light ? Appearance.Brush(176, 180, 190) : Appearance.Brush(110, 114, 124);
        var body = new Grid { Padding = new Thickness(9, 8, 9, 8), RowSpacing = 7 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        for (var i = 0; i < 3; i++) dots.Children.Add(new Border { Width = 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = ink });
        body.Children.Add(dots);
        var lines = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(new Border { Height = 4, Width = 30, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = ink });
        lines.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = ink, Opacity = .6 });
        lines.Children.Add(new Border { Height = 4, Width = 22, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = ink, Opacity = .6 });
        Grid.SetRow(lines, 1);
        body.Children.Add(lines);
        return new Border
        {
            Background = light ? Appearance.Brush(238, 239, 242) : Appearance.Brush(30, 31, 35),
            CornerRadius = left switch
            {
                true => new CornerRadius(8, 0, 0, 8), false => new CornerRadius(0, 8, 8, 0), _ => new CornerRadius(8)
            },
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1), Child = body
        };
    }

    private void UpdateThemeChoices()
    {
        foreach (var (card, check, choice) in themeChoices)
        {
            var selected = theme == choice;
            Appearance.SetTone(card, selected ? ButtonTone.Selected : ButtonTone.Raised);
            if (selected)
            {
                card.BorderBrush = Appearance.Text;
                card.BorderThickness = new Thickness(1.5);
            }
            ProductMotion.Opacity(check, selected ? 1 : 0, ProductMotion.Hover);
            AutomationProperties.SetItemStatus(card, selected ? "Selected" : "");
        }
    }

    private static ToggleSwitch Switch(string name, bool enabled)
    {
        var control = new ToggleSwitch { IsOn = enabled, OffContent = null, OnContent = null, MinWidth = 0 };
        AutomationProperties.SetName(control, name);
        return control;
    }

    private static Border Group(params FrameworkElement[] rows)
    {
        var stack = new StackPanel { Padding = new Thickness(18, 2, 14, 2) };
        foreach (var row in rows)
        {
            if (stack.Children.Count > 0) stack.Children.Add(Appearance.Rule());
            stack.Children.Add(row);
        }
        return new Border
        {
            Child = stack, Background = Appearance.Tint, BorderBrush = Appearance.Line,
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1)
        };
    }

    private static Grid Row(string name, string? hint, FrameworkElement value)
    {
        var row = new Grid { ColumnSpacing = 16, MinHeight = 60, Padding = new Thickness(0, 10, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
        var title = Appearance.Label(name, 14);
        title.TextWrapping = TextWrapping.Wrap;
        labels.Children.Add(title);
        if (hint is not null)
        {
            var detail = Appearance.Label(hint, 12, Appearance.Muted);
            detail.TextWrapping = TextWrapping.Wrap;
            labels.Children.Add(detail);
        }
        row.Children.Add(labels);
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }
}
