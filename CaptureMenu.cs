using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace RealCaptureX;

internal sealed record MenuAction(string Label, Glyph Icon, Action Execute, string? Shortcut = null, bool Destructive = false);
internal sealed record MenuSection(string? Label, params MenuAction[] Actions);

internal sealed class CaptureMenu : StackPanel
{
    private readonly List<Button> rows = [];

    public CaptureMenu(string? title, MenuSection[] sections, Action<Action> invoke)
    {
        Spacing = 2;
        Padding = new Thickness(7);
        if (title is not null)
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 9, Margin = new Thickness(7, 3, 5, 6)
            };
            header.Children.Add(Appearance.Mark(26));
            header.Children.Add(Appearance.Label(title, 13, strong: true));
            Children.Add(header);
        }

        foreach (var section in sections)
        {
            if (section.Label is { } sectionName)
            {
                var label = Appearance.Label(sectionName, 11, Appearance.Muted, strong: true);
                label.Margin = new Thickness(9, 6, 0, 1);
                Children.Add(label);
            }
            else if (Children.Count > 0)
            {
                var rule = Appearance.Rule();
                rule.Margin = new Thickness(6, 4, 6, 4);
                Children.Add(rule);
            }

            foreach (var action in section.Actions)
            {
                var row = Appearance.Action(action.Label, () => invoke(action.Execute));
                row.Height = 28;
                row.Padding = new Thickness(8, 0, 8, 0);
                row.HorizontalAlignment = HorizontalAlignment.Stretch;
                row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                var ink = action.Destructive ? Appearance.Danger : Appearance.Text;
                var layout = new Grid { ColumnSpacing = 9 };
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.ColumnDefinitions.Add(new ColumnDefinition());
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                layout.Children.Add(Symbols.Draw(action.Icon, 16, action.Destructive ? ink : Appearance.Muted));
                var label = Appearance.Label(action.Label, 12, ink);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                Grid.SetColumn(label, 1);
                layout.Children.Add(label);
                if (action.Shortcut is not null)
                {
                    var key = Appearance.Key(action.Shortcut.Replace("+", " "));
                    Grid.SetColumn(key, 2);
                    layout.Children.Add(key);
                    AutomationProperties.SetHelpText(row, action.Shortcut);
                }
                row.Content = layout;
                rows.Add(row);
                Children.Add(row);
            }
        }

        KeyDown += OnNavigation;
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

    internal static Flyout Flyout(params MenuSection[] sections)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
            ShouldConstrainToRootBounds = false
        };
        var menu = new CaptureMenu(null, sections, execute =>
        {
            flyout.Hide();
            execute();
        }) { MinWidth = 224 };
        var frame = Appearance.Frame(menu, 13);
        frame.Background = Appearance.Surface;
        flyout.Content = frame;
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Appearance.Clear));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        flyout.FlyoutPresenterStyle = style;
        flyout.Opened += (_, _) => menu.FocusFirst();
        return flyout;
    }

    internal static MenuAction Mode(CaptureCommand command, string label, Glyph glyph,
        Action<CaptureCommand> execute, string? shortcut = null) =>
        new(label, glyph, () => execute(command), shortcut, command == CaptureCommand.Exit);
}
