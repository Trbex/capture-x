using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace RealCaptureX;

internal sealed class QuickAccessWindow : Window
{
    private readonly Grid root = new() { RequestedTheme = ElementTheme.Dark, Background = Appearance.Surface };
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock metadata = Appearance.Label("", 11, Appearance.Muted);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly Func<CaptureEntry, Task> open;
    private CaptureEntry? entry;
    private double timeoutSeconds;
    private bool pointerInside;
    private bool dragging;
    private int revision;
    private readonly Button previous;
    private readonly Button next;

    public QuickAccessWindow(Func<CaptureEntry, Task> open, Func<CaptureEntry, Task> copy, Func<CaptureEntry, Task> save,
        Func<CaptureEntry, Task> pin, Func<CaptureEntry, Task> text, Action<int> browse, Action dismiss)
    {
        this.open = open;
        Title = "REAL Capture X — Quick Access";
        NativeWindow.ToolWindow(this);
        timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => dismiss();

        var stack = new StackPanel { Spacing = 7, Padding = new Thickness(8) };
        root.Children.Add(stack);
        var picture = new Border
        {
            Height = 136, CornerRadius = new CornerRadius(7), Background = Appearance.Panel, Child = preview,
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1), CanDrag = true
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picture, "Screenshot preview");
        ToolTipService.SetToolTip(picture, "Open screenshot · Drag to another app");
        picture.Tapped += async (_, _) => await UseAsync(this.open, null);
        picture.DragStarting += OnDragStarting;
        picture.DropCompleted += (_, _) => { dragging = false; RestartTimer(); };
        stack.Children.Add(picture);

        var commands = new Grid { ColumnSpacing = 4 };
        commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commands.ColumnDefinitions.Add(new ColumnDefinition());
        commands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        actions.Children.Add(Appearance.IconButton("\uE8C8", "Copy image", () => _ = UseAsync(copy, "Copied")));
        actions.Children.Add(Appearance.IconButton("\uE74E", "Save PNG as", () => _ = UseAsync(save, null)));
        actions.Children.Add(Appearance.IconButton("\uE70F", "Annotate screenshot", () => _ = UseAsync(open, null)));
        var more = Appearance.IconButton("\uE712", "More screenshot actions");
        var moreMenu = new MenuFlyout();
        var pinAction = new MenuFlyoutItem { Text = "Pin screenshot", Icon = Appearance.Icon("\uE840") };
        var recognizeAction = new MenuFlyoutItem { Text = "Recognize and copy text", Icon = Appearance.Icon("\uE8D2") };
        pinAction.Click += (_, _) => _ = UseAsync(pin, "Pinned");
        recognizeAction.Click += (_, _) => _ = UseAsync(text, "Text copied");
        moreMenu.Items.Add(pinAction);
        moreMenu.Items.Add(recognizeAction);
        moreMenu.Opened += (_, _) => timer.Stop();
        moreMenu.Closed += (_, _) => RestartTimer();
        more.Flyout = moreMenu;
        actions.Children.Add(more);
        commands.Children.Add(actions);
        metadata.TextAlignment = TextAlignment.Center;
        metadata.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(metadata, 1);
        commands.Children.Add(metadata);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        previous = Appearance.IconButton("\uE76B", "Older screenshot", () => browse(1));
        next = Appearance.IconButton("\uE76C", "Newer screenshot", () => browse(-1));
        navigation.Children.Add(previous);
        navigation.Children.Add(next);
        navigation.Children.Add(Appearance.IconButton("\uE711", "Dismiss preview", dismiss));
        Grid.SetColumn(navigation, 2);
        commands.Children.Add(navigation);
        stack.Children.Add(commands);
        root.PointerEntered += (_, _) => { pointerInside = true; timer.Stop(); };
        root.PointerExited += (_, _) => { pointerInside = false; RestartTimer(); };
        Content = root;
    }

    public async Task DisplayAsync(CaptureEntry capture, nint monitor, int index, int count,
        double seconds, double anchorX, double anchorY, bool animate)
    {
        var current = ++revision;
        var source = new BitmapImage { DecodePixelWidth = Math.Min(568, capture.Width) };
        using (var stream = await (await StorageFile.GetFileFromPathAsync(capture.Path)).OpenReadAsync())
            await source.SetSourceAsync(stream);
        if (current != revision) return;

        entry = capture;
        preview.Source = source;
        metadata.Text = count > 1 ? $"{index + 1} / {count}" : $"{capture.Width} × {capture.Height}";
        ToolTipService.SetToolTip(metadata, $"{capture.Width} × {capture.Height} · {capture.Created.ToLocalTime():g}");
        previous.IsEnabled = index + 1 < count;
        next.IsEnabled = index > 0;
        timeoutSeconds = seconds;
        NativeWindow.Place(this, monitor, 306, 190, anchorX, anchorY);
        var visual = ElementCompositionPreview.GetElementVisual(root);
        if (animate && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0, 0.1f);
            animation.InsertKeyFrame(1, 1);
            animation.Duration = TimeSpan.FromMilliseconds(165);
            visual.StartAnimation("Opacity", animation);
        }
        AppWindow.Show(false);
        RestartTimer();
    }

    public void Suspend()
    {
        revision++;
        timer.Stop();
        pointerInside = false;
        AppWindow.Hide();
    }

    private void RestartTimer()
    {
        timer.Stop();
        if (pointerInside || dragging || timeoutSeconds <= 0 || !AppWindow.IsVisible) return;
        timer.Interval = TimeSpan.FromSeconds(timeoutSeconds);
        timer.Start();
    }

    private async Task UseAsync(Func<CaptureEntry, Task> action, string? feedback)
    {
        if (entry is not { } capture) return;
        var current = revision;
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
