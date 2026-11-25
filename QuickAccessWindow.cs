using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace RealCaptureX;

internal sealed class QuickAccessWindow : Window
{
    private readonly Border frame;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock title = Appearance.Label("", 12, strong: true);
    private readonly TextBlock metadata = Appearance.Label("", 11, Appearance.Muted);
    private readonly TextBlock counter = Appearance.Label("", 10, Appearance.Muted);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private CaptureEntry? entry;
    private double timeoutSeconds;
    private bool pointerInside;
    private bool dragging;
    private bool menuOpen;
    private bool dismissing;
    private int busy;
    private int revision;
    private readonly Button previous;
    private readonly Button next;
    private readonly StackPanel navigation;

    public QuickAccessWindow(Func<CaptureEntry, Task> open, Func<CaptureEntry, Task> copy, Func<CaptureEntry, Task> save,
        Func<CaptureEntry, Task> pin, Func<CaptureEntry, Task> text, Action<int> browse, Action dismiss)
    {
        Title = "REAL Capture X — Quick Access";
        NativeWindow.ToolWindow(this);
        timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => dismiss();

        var layout = new Grid { RowSpacing = 7, Padding = new Thickness(9, 8, 9, 9) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) });
        frame = Appearance.Frame(layout);

        var header = new Grid { ColumnSpacing = 7 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Appearance.Mark(27));
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        title.TextTrimming = metadata.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetLiveSetting(metadata, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        details.Children.Add(title);
        details.Children.Add(metadata);
        Grid.SetColumn(details, 1);
        header.Children.Add(details);
        navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        previous = Appearance.IconButton(Glyph.Back, "Older screenshot", () => browse(1), 24);
        next = Appearance.IconButton(Glyph.Next, "Newer screenshot", () => browse(-1), 24);
        navigation.Children.Add(previous);
        navigation.Children.Add(counter);
        navigation.Children.Add(next);
        Grid.SetColumn(navigation, 2);
        header.Children.Add(navigation);
        var close = Appearance.IconButton(Glyph.Close, "Dismiss preview", dismiss, 27);
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        layout.Children.Add(header);

        var mount = new Border
        {
            CornerRadius = new CornerRadius(8), Background = Appearance.Canvas, Child = preview,
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1), CanDrag = true,
            Padding = new Thickness(5)
        };
        AutomationProperties.SetName(mount, "Screenshot preview");
        ToolTipService.SetToolTip(mount, "Annotate screenshot · Drag to another app");
        mount.Tapped += async (_, _) => await UseAsync(open, null);
        mount.DragStarting += OnDragStarting;
        mount.DropCompleted += (_, _) => { dragging = false; RestartTimer(); };
        Grid.SetRow(mount, 1);
        layout.Children.Add(mount);

        var actions = new Grid { ColumnSpacing = 5 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copyButton = Appearance.Action("Copy", () => _ = UseAsync(copy, "Copied to clipboard"), Glyph.Copy, ButtonTone.Primary);
        AutomationProperties.SetName(copyButton, "Copy image");
        copyButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        actions.Children.Add(copyButton);
        var edit = Appearance.Action("Annotate", () => _ = UseAsync(open, null), Glyph.Pen, ButtonTone.Raised);
        AutomationProperties.SetName(edit, "Annotate screenshot");
        edit.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(edit, 1);
        actions.Children.Add(edit);
        var saveButton = Appearance.IconButton(Glyph.Save, "Save PNG as", () => _ = UseAsync(save, null));
        Grid.SetColumn(saveButton, 2);
        actions.Children.Add(saveButton);
        var more = Appearance.IconButton(Glyph.More, "More screenshot actions");
        var flyout = CaptureMenu.Flyout(new MenuSection(null,
            new MenuAction("Pin screenshot", Glyph.Pin, () => _ = UseAsync(pin, "Pinned to screen")),
            new MenuAction("Recognize and copy text", Glyph.Scan, () => _ = UseAsync(text, "Text copied"))));
        flyout.Opened += (_, _) => { menuOpen = true; timer.Stop(); };
        flyout.Closed += (_, _) => { menuOpen = false; RestartTimer(); };
        more.Flyout = flyout;
        Grid.SetColumn(more, 3);
        actions.Children.Add(more);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        frame.PointerEntered += (_, _) => { pointerInside = true; timer.Stop(); };
        frame.PointerExited += (_, _) => { pointerInside = false; RestartTimer(); };
        frame.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Escape: dismiss(); break;
                case VirtualKey.Left when previous.IsEnabled: browse(1); break;
                case VirtualKey.Right when next.IsEnabled: browse(-1); break;
                default: return;
            }
            e.Handled = true;
        };
        Appearance.SetContent(this, frame, floating: true);
    }

    public async Task DisplayAsync(CaptureEntry capture, nint monitor, int index, int count,
        double seconds, double anchorX, double anchorY, bool animate)
    {
        var current = ++revision;
        dismissing = false;
        frame.IsHitTestVisible = true;
        timer.Stop();
        var source = new BitmapImage();
        if (capture.Width >= capture.Height) source.DecodePixelWidth = Math.Min(650, capture.Width);
        else source.DecodePixelHeight = Math.Min(420, capture.Height);
        using (var stream = await (await StorageFile.GetFileFromPathAsync(capture.Path)).OpenReadAsync())
            await source.SetSourceAsync(stream);
        if (current != revision) return;

        entry = capture;
        preview.Source = source;
        title.Text = CaptureLabel.Of(capture.Mode);
        metadata.Text = $"{capture.Width} × {capture.Height} px";
        ToolTipService.SetToolTip(metadata, $"{capture.Width} × {capture.Height} px · {capture.Created.ToLocalTime():g}");
        counter.Text = $"{index + 1}/{count}";
        navigation.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
        previous.IsEnabled = index + 1 < count;
        next.IsEnabled = index > 0;
        timeoutSeconds = seconds;

        var visible = AppWindow.IsVisible;
        if (!visible || animate)
        {
            var height = (int)Math.Clamp(285.0 * capture.Height / capture.Width + 106, 197, 247);
            NativeWindow.Place(this, monitor, 314, height, anchorX, anchorY);
        }
        ProductMotion.Enter(frame, animate && !visible);
        if (visible && !animate) ProductMotion.Reveal(preview);
        AppWindow.Show(false);
        RestartTimer();
    }

    public void HideNow()
    {
        revision++;
        dismissing = false;
        timer.Stop();
        pointerInside = false;
        AppWindow.Hide();
    }

    public async Task DismissAsync()
    {
        if (!AppWindow.IsVisible || dismissing) return;
        var current = ++revision;
        dismissing = true;
        timer.Stop();
        frame.IsHitTestVisible = false;
        await ProductMotion.ExitAsync(frame);
        if (current == revision) HideNow();
    }

    private void RestartTimer()
    {
        timer.Stop();
        if (pointerInside || dragging || menuOpen || dismissing || busy > 0 || timeoutSeconds <= 0 || !AppWindow.IsVisible) return;
        timer.Interval = TimeSpan.FromSeconds(timeoutSeconds);
        timer.Start();
    }

    private async Task UseAsync(Func<CaptureEntry, Task> action, string? feedback)
    {
        if (entry is not { } capture) return;
        var current = revision;
        busy++;
        timer.Stop();
        try
        {
            await action(capture);
            if (feedback is not null && revision == current) metadata.Text = feedback;
        }
        catch (Exception ex)
        {
            if (revision == current)
            {
                metadata.Text = "Action failed";
                ToolTipService.SetToolTip(metadata, ex.Message);
            }
        }
        finally
        {
            busy--;
            RestartTimer();
        }
    }

    private async void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (entry is not { } capture)
        {
            args.Cancel = true;
            return;
        }
        var deferral = args.GetDeferral();
        dragging = true;
        timer.Stop();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(capture.Path);
            args.AllowedOperations = DataPackageOperation.Copy;
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.Data.SetStorageItems([file], readOnly: true);
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            dragging = false;
            metadata.Text = "Drag failed";
            ToolTipService.SetToolTip(metadata, ex.Message);
            RestartTimer();
        }
        finally
        {
            deferral.Complete();
        }
    }
}

internal static class CaptureLabel
{
    internal static string Of(string mode) => mode switch
    {
        "Region" => "Region screenshot",
        "Window" => "Window screenshot",
        "Display" => "Display screenshot",
        "Scroll" => "Scrolling screenshot",
        "ScrollHorizontal" => "Wide scrolling screenshot",
        "Edited" => "Edited screenshot",
        _ => "Screenshot"
    };
}
