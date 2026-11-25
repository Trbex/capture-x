using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace RealCaptureX;

internal sealed class SettingsWindow : Window
{
    private readonly TextBlock status = Appearance.Label("", 12, Appearance.Muted);
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel rail = new() { Padding = new Thickness(10, 13, 10, 12), Spacing = 3 };
    private readonly List<Button> categories = [];
    private readonly List<Button> themeChoices = [];
    private ProductTheme theme;
    private ProductTheme savedTheme;

    public SettingsWindow(CaptureSettings initial, Action<CaptureSettings> save)
    {
        Title = "REAL Capture X — Settings";
        theme = savedTheme = initial.Theme;
        var root = new Grid { Background = Appearance.Surface };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(53) });
        root.Children.Add(Appearance.Rule());

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.Children.Add(new Border
        {
            Child = rail, Background = Appearance.Panel, BorderBrush = Appearance.Line,
            BorderThickness = new Thickness(0, 0, 1, 0)
        });
        Grid.SetColumn(scroll, 1);
        workspace.Children.Add(scroll);
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);
        var rule = Appearance.Rule();
        Grid.SetRow(rule, 2);
        root.Children.Add(rule);

        var cursor = Switch("Include pointer in screenshots", initial.IncludePointer);
        var showPreview = Switch("Show Quick Access after capture", initial.ShowPreview);
        var autoCopy = Switch("Copy screenshots automatically", initial.AutoCopy);
        var corner = new ComboBox
        {
            ItemsSource = new[] { "Bottom left", "Bottom right", "Top left", "Top right" },
            SelectedIndex = initial.Corner, Width = 134
        };
        AutomationProperties.SetName(corner, "Preview corner");
        var timeout = new NumberBox
        {
            Value = initial.Timeout, Minimum = 0, Maximum = 600, Width = 104,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        AutomationProperties.SetName(timeout, "Dismiss after seconds");
        var shortcuts = new[] { "Capture region", "Capture window", "Capture display", "Open toolbar", "Restore Quick Access" };
        var keys = shortcuts.Select((label, index) =>
        {
            var field = new TextBox { Text = initial.KeyBindings[index], MaxLength = 32, Width = 150 };
            AutomationProperties.SetName(field, label);
            return field;
        }).ToArray();

        var appearance = Page("Appearance");
        appearance.Children.Add(Appearance.Label("Theme", 13, strong: true));
        var swatches = new Grid { ColumnSpacing = 8 };
        foreach (var choice in Enum.GetValues<ProductTheme>())
        {
            swatches.ColumnDefinitions.Add(new ColumnDefinition());
            var card = ThemeCard(choice);
            Grid.SetColumn(card, (int)choice);
            swatches.Children.Add(card);
            themeChoices.Add(card);
        }
        appearance.Children.Add(swatches);
        appearance.Children.Add(Group(Row("Include pointer in screenshots", null, cursor)));
        UpdateThemeChoices();

        var quick = Page("Quick Access");
        quick.Children.Add(Group(
            Row("Show after capture", null, showPreview),
            Row("Copy automatically", null, autoCopy),
            Row("Preview corner", null, corner),
            Row("Dismiss after seconds", "Set to 0 to keep previews visible.", timeout)));

        var keyboard = Page("Keyboard shortcuts");
        keyboard.Children.Add(Appearance.Label("Use Ctrl or Alt with a letter, number, or function key.", 12, Appearance.Muted));
        keyboard.Children.Add(Group(shortcuts.Select((label, index) => Row(label, null, keys[index])).ToArray()));

        AddCategory("Appearance", Glyph.System, appearance);
        AddCategory("Quick Access", Glyph.Image, quick);
        AddCategory("Shortcuts", Glyph.Keyboard, keyboard);
        Select(categories[0], appearance);

        var footer = new Grid { Padding = new Thickness(15, 0, 15, 0), ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        footer.Children.Add(status);
        var apply = Appearance.Action("Save settings", () =>
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
                    Theme = theme
                });
                savedTheme = theme;
                status.Text = "Settings saved";
            }
            catch (Exception ex) { status.Text = ex.Message; }
            ToolTipService.SetToolTip(status, status.Text);
        }, Glyph.Check, ButtonTone.Primary);
        Grid.SetColumn(apply, 1);
        footer.Children.Add(apply);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Appearance.SetContent(this, root, caption: "Settings");
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 710, 501, .5, .47);
        Closed += (_, _) =>
        {
            if (savedTheme != theme) Appearance.SetTheme(savedTheme);
        };
    }

    private static StackPanel Page(string title)
    {
        var page = new StackPanel { Padding = new Thickness(23, 18, 23, 20), Spacing = 13 };
        page.Children.Add(Appearance.Label(title, 24, strong: true));
        return page;
    }

    private void AddCategory(string label, Glyph icon, FrameworkElement content)
    {
        var button = Appearance.Action(label, icon: icon);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Height = 36;
        button.Click += (_, _) => Select(button, content);
        categories.Add(button);
        rail.Children.Add(button);
    }

    private void Select(Button active, FrameworkElement content)
    {
        foreach (var button in categories)
        {
            var selected = ReferenceEquals(button, active);
            Appearance.SetTone(button, selected ? ButtonTone.Selected : ButtonTone.Quiet);
            AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        }
        if (ReferenceEquals(scroll.Content, content)) return;
        scroll.Content = content;
        if (scroll.IsLoaded)
        {
            scroll.ChangeView(null, 0, null, disableAnimation: true);
            ProductMotion.Reveal(content);
        }
    }

    private Button ThemeCard(ProductTheme choice)
    {
        var label = choice.ToString();
        var button = Appearance.Action(label, () =>
        {
            theme = choice;
            Appearance.SetTheme(theme);
            UpdateThemeChoices();
        });
        button.Padding = new Thickness(7, 7, 7, 6);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Height = 85;
        var panel = new StackPanel { Spacing = 6 };
        var miniature = new Grid { Height = 43, ColumnSpacing = 3 };
        if (choice == ProductTheme.System)
        {
            miniature.ColumnDefinitions.Add(new ColumnDefinition());
            miniature.ColumnDefinitions.Add(new ColumnDefinition());
            miniature.Children.Add(Sample(true));
            var dark = Sample(false);
            Grid.SetColumn(dark, 1);
            miniature.Children.Add(dark);
        }
        else miniature.Children.Add(Sample(choice == ProductTheme.Light));
        panel.Children.Add(miniature);
        panel.Children.Add(Appearance.Label(label, 11, strong: true));
        button.Content = panel;
        return button;
    }

    private static Border Sample(bool light)
    {
        var ink = light ? Appearance.Brush(107, 73, 52) : Appearance.Brush(235, 185, 143);
        var rows = new StackPanel { Padding = new Thickness(6), Spacing = 4 };
        rows.Children.Add(new Border
        {
            Background = ink, Width = 13, Height = 3, CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left
        });
        rows.Children.Add(new Border
        {
            Background = light ? Appearance.Brush(230, 219, 207) : Appearance.Brush(73, 65, 66),
            Height = 13, CornerRadius = new CornerRadius(3)
        });
        return new Border
        {
            Background = light ? Appearance.Brush(255, 253, 248) : Appearance.Brush(41, 36, 39),
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = rows
        };
    }

    private void UpdateThemeChoices()
    {
        for (var index = 0; index < themeChoices.Count; index++)
        {
            var selected = (int)theme == index;
            Appearance.SetTone(themeChoices[index], selected ? ButtonTone.Selected : ButtonTone.Raised);
            AutomationProperties.SetItemStatus(themeChoices[index], selected ? "Selected" : "");
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
        var stack = new StackPanel { Padding = new Thickness(11, 0, 11, 0) };
        foreach (var row in rows)
        {
            if (stack.Children.Count > 0) stack.Children.Add(Appearance.Rule());
            stack.Children.Add(row);
        }
        return new Border
        {
            Child = stack, Background = Appearance.Panel, BorderBrush = Appearance.Line,
            CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1)
        };
    }

    private static Grid Row(string name, string? hint, FrameworkElement value)
    {
        var row = new Grid { ColumnSpacing = 10, MinHeight = 53, Padding = new Thickness(2, 6, 2, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        var title = Appearance.Label(name, 12, strong: true);
        title.TextWrapping = TextWrapping.Wrap;
        labels.Children.Add(title);
        if (hint is not null)
        {
            var detail = Appearance.Label(hint, 11, Appearance.Muted);
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
