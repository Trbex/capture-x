using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace RealCaptureX;

internal sealed record MenuAction(string Label, Glyph Icon, Action Execute, string? Shortcut = null, bool Destructive = false);
internal sealed record MenuSection(string? Label, params MenuAction[] Actions);
internal sealed record MenuHeader(string Title, params MenuAction[] Tools);

/// <summary>Grouped command list used by every popup menu (tray, quick menu, context menus).</summary>
internal sealed class CaptureMenu : StackPanel
{
    private readonly List<Button> rows = [];

    public CaptureMenu(MenuHeader? header, MenuSection[] sections, Action<Action> invoke)
    {
        Spacing = 0;
        Padding = new Thickness(6, 6, 6, 6);
        if (header is not null)
        {
            var bar = new Grid { Margin = new Thickness(8, 4, 2, 6), ColumnSpacing = 10 };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(Appearance.Mark(28));
            var title = Appearance.Label(header.Title, 14, strong: true);
            Grid.SetColumn(title, 1);
            bar.Children.Add(title);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var tool in header.Tools)
                tools.Children.Add(Appearance.IconButton(tool.Icon, tool.Label, () => invoke(tool.Execute), 30));
            Grid.SetColumn(tools, 2);
            bar.Children.Add(tools);
            Children.Add(bar);
            var rule = Appearance.Rule();
            rule.Margin = new Thickness(2, 0, 2, 4);
            Children.Add(rule);
        }

        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            if (index > 0)
            {
                var rule = Appearance.Rule();
                rule.Margin = new Thickness(10, 5, 10, 5);
                Children.Add(rule);
            }
            if (section.Label is { } sectionName)
            {
                var label = Appearance.Label(sectionName.ToUpperInvariant(), 11, Appearance.Muted, strong: true);
                label.CharacterSpacing = 40;
                label.Margin = new Thickness(10, 4, 0, 4);
                Children.Add(label);
            }
            foreach (var action in section.Actions) Children.Add(Row(action, invoke));
        }

        KeyDown += OnNavigation;
    }

    private Button Row(MenuAction action, Action<Action> invoke)
    {
        var row = new Button
        {
            Style = (Style)Application.Current.Resources["TileButton"], Background = Appearance.Clear,
            BorderThickness = new Thickness(0), Height = 34, Padding = new Thickness(0), CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            UseSystemFocusVisuals = false
        };
        AutomationProperties.SetName(row, action.Label);
        var ink = Appearance.Text;
        var face = new Grid();
        var highlight = new Border { CornerRadius = new CornerRadius(8), Background = Appearance.Tint };
        ProductMotion.Hide(highlight);
        face.Children.Add(highlight);
        var layout = new Grid { ColumnSpacing = 12, Padding = new Thickness(10, 0, 8, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(Symbols.Draw(action.Icon, 18, ink));
        var label = Appearance.Label(action.Label, 13, ink);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 1);
        layout.Children.Add(label);
        if (action.Shortcut is not null)
        {
            var key = Appearance.Key(Appearance.ShortcutText(action.Shortcut));
            Grid.SetColumn(key, 2);
            layout.Children.Add(key);
            AutomationProperties.SetHelpText(row, action.Shortcut);
        }
        face.Children.Add(layout);
        row.Content = face;
        row.Click += (_, _) => invoke(action.Execute);
        row.PointerEntered += (_, _) => row.Focus(FocusState.Pointer);
        // The highlight lands on the row the instant the pointer reaches it and lingers briefly as it leaves,
        // so sweeping down the menu reads as one continuous trail rather than flicker.
        row.GotFocus += (_, _) => ProductMotion.Opacity(highlight, 1, TimeSpan.Zero);
        row.LostFocus += (_, _) => ProductMotion.Opacity(highlight, 0, TimeSpan.FromMilliseconds(130));
        rows.Add(row);
        return row;
    }

    public void FocusFirst() => rows[0].Focus(FocusState.Programmatic);


    private void OnNavigation(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)) return;
        var index = FocusManager.GetFocusedElement(XamlRoot) is Button button ? rows.IndexOf(button) : -1;
        var next = e.Key switch
        {
            VirtualKey.Home => 0,
            VirtualKey.End => rows.Count - 1,
            VirtualKey.Up => index <= 0 ? rows.Count - 1 : index - 1,
            _ => (index + 1) % rows.Count
        };
        rows[next].Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    internal static MenuAction Mode(CaptureCommand command, string label, Glyph glyph,
        Action<CaptureCommand> execute, string? shortcut = null) =>
        new(label, glyph, () => execute(command), shortcut, command == CaptureCommand.Exit);
}
