using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace RealCaptureX;

internal sealed class HistoryWindow : Window
{
    private readonly CaptureStore store;
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Border empty;
    private readonly TextBlock previewMessage = Appearance.Label("Select a capture", 12, Appearance.Muted);
    private readonly TextBlock count = Appearance.Label("", 11, Appearance.Muted);
    private readonly TextBlock detail = Appearance.Label("", 12, Appearance.Muted);
    private readonly TextBlock status = Appearance.Label("", 11, Appearance.Muted);
    private readonly Button shots;
    private readonly Button videos;
    private readonly Button openButton;
    private readonly StackPanel shotActions = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly Func<CaptureEntry, Task> openImage;
    private readonly Func<string, Task> openVideo;
    private bool videoMode;
    private bool closed;
    private int previewRevision;

    public HistoryWindow(CaptureStore store, Func<CaptureEntry, Task> openImage, Func<string, Task> openVideo,
        Func<CaptureEntry, Task> copy, Func<CaptureEntry, Task> save, Func<CaptureEntry, Task> pin, Action deleted)
    {
        this.store = store;
        this.openImage = openImage;
        this.openVideo = openVideo;
        Title = "REAL Capture X — Capture history";
        var root = new Grid { Background = Appearance.Surface };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(55) });
        root.Children.Add(Appearance.Rule());

        var header = new Grid { Padding = new Thickness(18, 12, 16, 11), ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Appearance.Label("Capture history", 22, strong: true));
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        shots = Appearance.Action("Screenshots", () => Switch(false), Glyph.Image, ButtonTone.Selected);
        videos = Appearance.Action("Recordings", () => Switch(true), Glyph.Video);
        filters.Children.Add(shots);
        filters.Children.Add(videos);
        Grid.SetColumn(filters, 1);
        header.Children.Add(filters);
        Grid.SetRow(header, 1);
        root.Children.Add(header);

        var split = new Grid { ColumnSpacing = 12, Padding = new Thickness(12, 0, 12, 12) };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(234) });
        split.ColumnDefinitions.Add(new ColumnDefinition());
        AutomationProperties.SetName(list, "Captures");
        list.Padding = new Thickness(4);
        list.SelectionChanged += async (_, _) => await PreviewAsync();
        list.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        var library = new Grid();
        library.RowDefinitions.Add(new RowDefinition());
        library.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
        library.Children.Add(list);
        count.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetRow(count, 1);
        library.Children.Add(count);
        split.Children.Add(new Border
        {
            Child = library, CornerRadius = new CornerRadius(10), Background = Appearance.Panel,
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1)
        });

        var previewArea = new Grid();
        previewArea.RowDefinitions.Add(new RowDefinition());
        previewArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) });
        preview.Margin = new Thickness(10);
        var previewHost = new Grid();
        previewHost.Children.Add(preview);
        var emptyBody = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        emptyBody.Children.Add(Symbols.Draw(Glyph.Image, 26, Appearance.Muted));
        emptyBody.Children.Add(previewMessage);
        empty = new Border { Child = emptyBody, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        previewHost.Children.Add(empty);
        previewArea.Children.Add(new Border
        {
            Child = previewHost, CornerRadius = new CornerRadius(10), Background = Appearance.Canvas,
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1)
        });
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.Margin = new Thickness(1, 4, 0, 0);
        Grid.SetRow(detail, 1);
        previewArea.Children.Add(detail);
        Grid.SetColumn(previewArea, 1);
        split.Children.Add(previewArea);
        Grid.SetRow(split, 2);
        root.Children.Add(split);
        var divider = Appearance.Rule();
        Grid.SetRow(divider, 3);
        root.Children.Add(divider);

        var footer = new Grid { Padding = new Thickness(13, 0, 13, 0), ColumnSpacing = 9 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(status);

        AddShotAction("Copy image", Glyph.Copy, copy, "Copied to clipboard");
        AddShotAction("Save PNG as", Glyph.Save, save);
        AddShotAction("Pin screenshot", Glyph.Pin, pin, "Pinned to screen");
        AddShotAction("Delete screenshot", Glyph.Trash, async entry =>
        {
            var prompt = new ContentDialog
            {
                XamlRoot = root.XamlRoot, Title = "Delete screenshot?",
                Content = "The saved capture will be moved to the Recycle Bin.",
                PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
            };
            if (await prompt.ShowAsync() != ContentDialogResult.Primary) return;
            await (await StorageFile.GetFileFromPathAsync(entry.Path)).DeleteAsync();
            deleted();
            Refresh();
        });
        Grid.SetColumn(shotActions, 1);
        footer.Children.Add(shotActions);
        openButton = Appearance.Action("Annotate", () => _ = OpenSelectedAsync(), Glyph.Pen, ButtonTone.Primary);
        Grid.SetColumn(openButton, 2);
        footer.Children.Add(openButton);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        Appearance.SetContent(this, root, caption: "History");
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 804, 527, .5, .47);
        Closed += (_, _) => { closed = true; previewRevision++; };
        Refresh();

        void AddShotAction(string name, Glyph glyph, Func<CaptureEntry, Task> action, string? feedback = null)
        {
            shotActions.Children.Add(Appearance.IconButton(glyph, name, () => _ = UseAsync(async () =>
            {
                if (Selected is not CaptureEntry entry) return;
                await action(entry);
                if (feedback is not null) status.Text = feedback;
            })));
        }
    }

    private object? Selected => (list.SelectedItem as ListViewItem)?.Tag;

    private void Switch(bool videosSelected)
    {
        if (videoMode == videosSelected) return;
        videoMode = videosSelected;
        Appearance.SetTone(shots, videoMode ? ButtonTone.Quiet : ButtonTone.Selected);
        Appearance.SetTone(videos, videoMode ? ButtonTone.Selected : ButtonTone.Quiet);
        AutomationProperties.SetItemStatus(shots, videoMode ? "" : "Selected");
        AutomationProperties.SetItemStatus(videos, videoMode ? "Selected" : "");
        shotActions.Visibility = videoMode ? Visibility.Collapsed : Visibility.Visible;
        var label = videoMode ? "Open in Studio" : "Annotate";
        openButton.Content = Appearance.ActionContent(label, videoMode ? Glyph.Studio : Glyph.Pen, Appearance.OnAccent);
        AutomationProperties.SetName(openButton, label);
        Refresh();
    }

    internal void Refresh()
    {
        var old = Selected;
        if (videoMode)
        {
            var folder = Path.Combine(CaptureStore.Root, "Recordings");
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.mp4").OrderDescending().ToArray() : [];
            list.ItemsSource = files.Select(path => Item(path, Path.GetFileNameWithoutExtension(path), "MP4 recording", Glyph.Video)).ToArray();
        }
        else list.ItemsSource = store.ReadAll().Select(entry => Item(entry, CaptureLabel.Of(entry.Mode),
            entry.Created.ToLocalTime().ToString("g"), Glyph.Image)).ToArray();

        var items = (ListViewItem[])list.ItemsSource;
        count.Text = $"{items.Length} {(videoMode ? "recordings" : "screenshots")}";
        list.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, old)) ?? items.FirstOrDefault();
        if (items.Length == 0) _ = PreviewAsync();
    }

    private static ListViewItem Item(object value, string name, string subtitle, Glyph icon)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(2, 4, 0, 4) };
        var thumb = new Border
        {
            Width = 46, Height = 32, Background = Appearance.Canvas, CornerRadius = new CornerRadius(6),
            BorderBrush = Appearance.Line, BorderThickness = new Thickness(1), Child = Symbols.Draw(icon, 16, Appearance.Muted)
        };
        if (value is CaptureEntry capture) _ = LoadThumbnailAsync(thumb, capture.Path);
        row.Children.Add(thumb);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, MaxWidth = 150 };
        var title = Appearance.Label(name, 12, strong: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        labels.Children.Add(title);
        labels.Children.Add(Appearance.Label(subtitle, 10, Appearance.Muted));
        row.Children.Add(labels);
        var item = new ListViewItem { Tag = value, Content = row, Padding = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(item, name + ", " + subtitle);
        return item;
    }

    /// <summary>Small decoded thumbnail (96 px wide) so the list stays light even with many captures.</summary>
    private static async Task LoadThumbnailAsync(Border target, string path)
    {
        try
        {
            var source = new BitmapImage { DecodePixelWidth = 96 };
            using var stream = await (await StorageFile.GetFileFromPathAsync(path)).OpenReadAsync();
            await source.SetSourceAsync(stream);
            target.Child = null;
            target.Background = new ImageBrush { ImageSource = source, Stretch = Stretch.UniformToFill };
            ProductMotion.Reveal(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
    }

    private async Task PreviewAsync()
    {
        var current = ++previewRevision;
        var selected = Selected;
        openButton.IsEnabled = selected is not null;
        shotActions.IsHitTestVisible = selected is CaptureEntry;
        foreach (var button in shotActions.Children.OfType<Button>()) button.IsEnabled = selected is CaptureEntry;
        preview.Source = null;
        status.Text = "";
        empty.Visibility = Visibility.Visible;
        previewMessage.Text = selected is null ? "No captures in this view" : "Loading preview…";
        detail.Text = selected switch
        {
            CaptureEntry entry => $"{CaptureLabel.Of(entry.Mode)}  ·  {entry.Width} × {entry.Height} px",
            string path => Path.GetFileName(path),
            _ => ""
        };
        if (selected is null) return;

        try
        {
            var source = new BitmapImage { DecodePixelWidth = 720 };
            if (selected is CaptureEntry frame)
            {
                var file = await StorageFile.GetFileFromPathAsync(frame.Path);
                using var stream = await file.OpenReadAsync();
                await source.SetSourceAsync(stream);
            }
            else if (selected is string path)
            {
                var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
                var composition = new MediaComposition();
                composition.Clips.Add(clip);
                using var stream = await composition.GetThumbnailAsync(TimeSpan.Zero, 432, 250, VideoFramePrecision.NearestFrame);
                await source.SetSourceAsync(stream);
            }
            if (closed || current != previewRevision) return;
            preview.Source = source;
            empty.Visibility = Visibility.Collapsed;
            ProductMotion.Reveal(preview);
        }
        catch (Exception)
        {
            if (!closed && current == previewRevision) previewMessage.Text = "Preview unavailable";
        }
    }

    private Task OpenSelectedAsync() => UseAsync(() => Selected switch
    {
        CaptureEntry entry => openImage(entry),
        string path => openVideo(path),
        _ => Task.CompletedTask
    });

    private async Task UseAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { status.Text = ex.Message; }
        ToolTipService.SetToolTip(status, status.Text);
    }
}
