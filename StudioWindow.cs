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
    private readonly NumberBox start = new() { Header = "In (s)", Minimum = 0, SmallChange = 0.1, Width = 104, Value = 0 };
    private readonly NumberBox end = new() { Header = "Out (s)", Minimum = 0, SmallChange = 0.1, Width = 104 };
    private readonly Slider volume = new() { Header = "Volume", Maximum = 100, Value = 100, Width = 104 };
    private readonly TextBlock status = Appearance.Label("Opening recording…", 12, Appearance.Muted);
    private readonly Button export = new() { Content = "Export MP4", IsEnabled = false };
    private StorageFile? file;
    private TimeSpan duration;
    private bool closed;

    public StudioWindow(string path)
    {
        this.path = path;
        Title = "REAL Capture X — Studio";
        Appearance.StyleTitleBar(this);
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = Appearance.Surface };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(player);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(14, 8, 14, 2) };
        var markIn = new Button { Content = "Set in", VerticalAlignment = VerticalAlignment.Bottom };
        var markOut = new Button { Content = "Set out", VerticalAlignment = VerticalAlignment.Bottom };
        markIn.Click += (_, _) => start.Value = Math.Round(player.MediaPlayer?.PlaybackSession.Position.TotalSeconds ?? 0, 3);
        markOut.Click += (_, _) => end.Value = Math.Round(player.MediaPlayer?.PlaybackSession.Position.TotalSeconds ?? 0, 3);
        controls.Children.Add(start);
        controls.Children.Add(markIn);
        controls.Children.Add(end);
        controls.Children.Add(markOut);
        controls.Children.Add(volume);
        volume.ValueChanged += (_, args) =>
        {
            if (player.MediaPlayer is { } media) media.Volume = args.NewValue / 100;
        };
        export.VerticalAlignment = VerticalAlignment.Bottom;
        export.Click += async (_, _) => await ExportAsync();
        controls.Children.Add(export);
        Grid.SetRow(controls, 1);
        root.Children.Add(controls);
        status.Margin = new Thickness(14, 4, 14, 10);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(status, 2);
        root.Children.Add(status);
        Content = root;
        NativeWindow.Place(this, NativeWindow.CursorMonitor(), 870, 580, 0.5, 0.43);
        Closed += (_, _) =>
        {
            closed = true;
            var media = player.MediaPlayer;
            player.Source = null;
            media?.Dispose();
        };
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
        status.Text = $"{selected.Name}  ·  {duration:mm\\:ss}";
        export.IsEnabled = duration > TimeSpan.Zero;
    }

    private async Task ExportAsync()
    {
        if (file is null) return;
        if (!double.IsFinite(start.Value) || !double.IsFinite(end.Value) || start.Value < 0 ||
            end.Value > duration.TotalSeconds || end.Value - start.Value < 0.1)
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
            clip.TrimTimeFromStart = TimeSpan.FromSeconds(start.Value);
            clip.TrimTimeFromEnd = duration - TimeSpan.FromSeconds(end.Value);
            clip.Volume = volume.Value / 100;
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
