using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace RealCaptureX;

internal sealed class StudioWindow : Window
{
    private readonly string path;
    private readonly MediaPlayerElement player = new() { AreTransportControlsEnabled = true, AutoPlay = false };
    private readonly NumberBox start = new() { Header = "In point (s)", Minimum = 0, SmallChange = 0.1, Width = 104, Value = 0 };
    private readonly NumberBox end = new() { Header = "Out point (s)", Minimum = 0, SmallChange = 0.1, Width = 104 };
    private readonly Slider volume = new() { Header = "Volume", Maximum = 100, Value = 100 };
    private readonly TextBlock status = Appearance.Label("Opening recording…", 12, Appearance.Muted);
    private readonly Button export = Appearance.Action("Export MP4", icon: Glyph.Save, tone: ButtonTone.Primary);
    private readonly TextBlock rangeLabel = Appearance.Label("Selection", 11, Appearance.Muted);
    private readonly ColumnDefinition before = new() { Width = new GridLength(0, GridUnitType.Star) };
    private readonly ColumnDefinition chosen = new() { Width = new GridLength(0, GridUnitType.Star) };
    private readonly ColumnDefinition after = new();
    private StorageFile? file;
    private TimeSpan duration;
    private bool closed;

    public StudioWindow(string path)
    {
        this.path = path;
        Title = "REAL Capture X — Studio";
        var root = new Grid { Background = Appearance.Surface };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        player.RequestedTheme = ElementTheme.Dark;
        player.TransportControls = new MediaTransportControls { IsCompact = true };
        var stage = new Border
        {
            Background = Appearance.Canvas, BorderBrush = Appearance.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Margin = new Thickness(13, 5, 13, 12), Padding = new Thickness(8),
            Child = player
        };
        root.Children.Add(stage);

        var edit = new StackPanel
        {
            Background = Appearance.Panel, Spacing = 10, Padding = new Thickness(14, 11, 14, 13)
        };
        var header = new Grid();
        header.Children.Add(Appearance.Label("Trim recording", 13, strong: true));
        rangeLabel.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(rangeLabel);
        edit.Children.Add(header);

        var track = new Grid { Height = 6, Background = Appearance.Canvas, CornerRadius = new CornerRadius(3) };
        track.ColumnDefinitions.Add(before);
        track.ColumnDefinitions.Add(chosen);
        track.ColumnDefinitions.Add(after);
        var highlight = new Border { Background = Appearance.Accent, CornerRadius = new CornerRadius(3) };
        Grid.SetColumn(highlight, 1);
        track.Children.Add(highlight);
        edit.Children.Add(track);

        var fields = new Grid { ColumnSpacing = 17 };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition());
        fields.Children.Add(TrimControl(start, "Set in", () => start.Value = PlaybackPosition()));
        var outPoint = TrimControl(end, "Set out", () => end.Value = PlaybackPosition());
        Grid.SetColumn(outPoint, 1);
        fields.Children.Add(outPoint);
        volume.MaxWidth = 238;
        volume.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(volume, 2);
        fields.Children.Add(volume);
        edit.Children.Add(fields);
        Grid.SetRow(edit, 1);
        root.Children.Add(edit);

        var footer = new Grid { Padding = new Thickness(14, 8, 14, 8), ColumnSpacing = 13 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(status);
        export.IsEnabled = false;
        export.Click += async (_, _) => await ExportAsync();
        Grid.SetColumn(export, 1);
        footer.Children.Add(export);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        start.ValueChanged += (_, _) => UpdateRange();
        end.ValueChanged += (_, _) => UpdateRange();
        volume.ValueChanged += (_, args) =>
        {
            if (player.MediaPlayer is { } media) media.Volume = args.NewValue / 100;
        };
        Appearance.SetContent(this, root, caption: "Studio");
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 870, 604, 0.5, 0.43);
        Closed += (_, _) =>
        {
            closed = true;
            var media = player.MediaPlayer;
            player.Source = null;
            media?.Dispose();
        };
    }

    private static Grid TrimControl(NumberBox input, string label, Action mark)
    {
        var row = new Grid { ColumnSpacing = 5 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(input);
        var button = Appearance.Action(label, mark, tone: ButtonTone.Raised);
        button.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return row;
    }

    private double PlaybackPosition() => Math.Round(player.MediaPlayer?.PlaybackSession.Position.TotalSeconds ?? 0, 3);

    private void UpdateRange()
    {
        var valid = duration > TimeSpan.Zero && double.IsFinite(start.Value) && double.IsFinite(end.Value)
            && start.Value >= 0 && end.Value >= start.Value && end.Value <= duration.TotalSeconds;
        before.Width = new GridLength(valid ? start.Value : 0, GridUnitType.Star);
        chosen.Width = new GridLength(valid ? end.Value - start.Value : 0, GridUnitType.Star);
        after.Width = new GridLength(valid ? Math.Max(0, duration.TotalSeconds - end.Value) : 1, GridUnitType.Star);
        rangeLabel.Text = valid ? $"{end.Value - start.Value:0.00} s selected" : "Selection";
    }

    public async Task LoadAsync()
    {
        var selected = await StorageFile.GetFileFromPathAsync(path);
        var clip = await MediaClip.CreateFromFileAsync(selected);
        if (closed) return;
        file = selected;
        duration = clip.OriginalDuration;
        start.Maximum = end.Maximum = duration.TotalSeconds;
        end.Value = duration.TotalSeconds;
        player.Source = MediaSource.CreateFromStorageFile(selected);
        UpdateRange();
        status.Text = $"{selected.Name}  ·  {duration:mm\\:ss}";
        export.IsEnabled = duration > TimeSpan.Zero;
    }

    private async Task ExportAsync()
    {
        if (file is null) return;
        var inPoint = start.Value;
        var outPoint = end.Value;
        var outputVolume = volume.Value / 100;
        if (!double.IsFinite(inPoint) || !double.IsFinite(outPoint) || inPoint < 0 ||
            outPoint > duration.TotalSeconds || outPoint - inPoint < 0.1)
        {
            status.Text = "Choose an in-point and an out-point at least 0.1 seconds apart.";
            return;
        }
        export.IsEnabled = false;
        string? pending = null;
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(path) + " edited",
                SuggestedStartLocation = PickerLocationId.VideosLibrary, DefaultFileExtension = ".mp4"
            };
#if DEBUG
            if (Environment.GetEnvironmentVariable("REAL_CAPTURE_X_INSPECT") == "1")
            {
                picker.SuggestedFolder = Path.Combine(CaptureStore.Root, "Exports");
                Directory.CreateDirectory(picker.SuggestedFolder);
            }
#endif
            picker.FileTypeChoices.Add("MP4 video", [".mp4"]);
            var selected = await picker.PickSaveFileAsync();
            if (selected is null || closed) return;
            if (Path.GetFullPath(selected.Path).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different file name to preserve the original recording.");
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(selected.Path)!);
            var temp = await folder.CreateFileAsync($".realcapture-{Guid.NewGuid():N}.mp4");
            pending = temp.Path;
            var clip = await MediaClip.CreateFromFileAsync(file);
            clip.TrimTimeFromStart = TimeSpan.FromSeconds(inPoint);
            clip.TrimTimeFromEnd = duration - TimeSpan.FromSeconds(outPoint);
            clip.Volume = outputVolume;
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            var profile = await MediaEncodingProfile.CreateFromFileAsync(file);
            status.Text = "Encoding edited video…";
            var outcome = await composition.RenderToFileAsync(temp, MediaTrimmingPreference.Precise, profile);
            if (outcome != Windows.Media.Transcoding.TranscodeFailureReason.None)
                throw new InvalidOperationException($"Windows could not export the video: {outcome}.");
            File.Move(pending, selected.Path, overwrite: true);
            status.Text = "Edited MP4 exported";
        }
        catch (Exception ex)
        {
            status.Text = string.IsNullOrWhiteSpace(ex.Message) ? $"MP4 export failed (0x{ex.HResult:X8})." : ex.Message;
            ToolTipService.SetToolTip(status, status.Text);
        }
        finally
        {
            if (pending is not null) File.Delete(pending);
            if (!closed) export.IsEnabled = true;
        }
    }
}
