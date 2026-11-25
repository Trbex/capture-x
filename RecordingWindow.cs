using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace RealCaptureX;

internal sealed class RecordingWindow : Window
{
    private readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stop = new();
    private readonly TextBlock status = Appearance.Label("", 12, Appearance.Muted);
    private readonly StackPanel options = new() { Spacing = 5 };
    private readonly CheckBox cursor = new() { Content = "Show pointer", IsChecked = true };
    private readonly CheckBox system = new() { Content = "System audio" };
    private readonly CheckBox microphone = new() { Content = "Microphone" };
    private readonly CheckBox camera = new() { Content = "Webcam inset" };
    private readonly CheckBox smartZoom = new() { Content = "Smart zoom on clicks" };
    private readonly Button start = new() { Content = "Start recording" };
    private readonly Button end = new() { Content = "Stop", IsEnabled = false };
    private readonly nint monitor;
    private readonly RectInt32 area;
    private bool running;
    private bool closed;

    public RecordingWindow(nint monitor, RectInt32 area)
    {
        this.monitor = monitor;
        this.area = area;
        Title = "REAL Capture X — Recording";
        NativeWindow.ToolWindow(this);
        var root = new StackPanel
        {
            RequestedTheme = ElementTheme.Dark, Background = Appearance.Surface,
            Padding = new Thickness(12, 8, 12, 8), Spacing = 6
        };
        root.Children.Add(Appearance.Label("Record screen", 14));
        status.Text = $"{area.Width} × {area.Height} · MP4";
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(status, status.Text);
        root.Children.Add(status);
        options.Children.Add(cursor);
        options.Children.Add(system);
        options.Children.Add(microphone);
        options.Children.Add(camera);
        options.Children.Add(smartZoom);
        root.Children.Add(options);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(start);
        row.Children.Add(end);
        row.Children.Add(Appearance.IconButton("\uE711", "Close recording controls", () =>
        {
            if (running) RequestStop(); else Close();
        }));
        root.Children.Add(row);
        Content = root;
        start.Click += async (_, _) => await StartAsync();
        end.Click += (_, _) => RequestStop();
        AppWindow.Closing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true;
            RequestStop();
        };
        Closed += (_, _) =>
        {
            closed = true;
            result.TrySetResult(null);
            stop.Dispose();
        };
    }

    public Task<string?> ShowAsync()
    {
        Place(compact: false);
        Activate();
        return result.Task;
    }

    private void Place(bool compact) => NativeWindow.Place(this, monitor, 308, compact ? 112 : 290, 1, 0);

    private void RequestStop()
    {
        if (stop.IsCancellationRequested) return;
        end.IsEnabled = false;
        status.Text = "Finishing MP4…";
        stop.Cancel();
    }

    private async Task StartAsync()
    {
        if (running) return;
        running = true;
        string? pending = null;
        var destination = Path.Combine(CaptureStore.Root, "Recordings");
        start.IsEnabled = false;
        foreach (var option in options.Children.OfType<CheckBox>()) option.IsEnabled = false;
        try
        {
            Directory.CreateDirectory(destination);
            pending = Path.Combine(destination, $".realcapture-{Guid.NewGuid():N}.tmp");
            using (File.Create(pending)) { }
            status.Text = "Preparing capture…";
            using var cameraSource = camera.IsChecked == true ? await RecordingCamera.OpenAsync() : null;
            using var audio = system.IsChecked == true || microphone.IsChecked == true
                ? new RecordingAudio(system.IsChecked == true, microphone.IsChecked == true) : null;
            if (stop.IsCancellationRequested) return;
            options.Visibility = Visibility.Collapsed;
            start.Visibility = Visibility.Collapsed;
            end.IsEnabled = true;
            status.Text = "Recording · Stop to open Studio";
            Place(compact: true);
#if DEBUG
            if (Environment.GetEnvironmentVariable("REAL_CAPTURE_X_INSPECT") != "1")
#endif
                NativeWindow.ExcludeFromCapture(this);
            await ScreenRecorder.RecordAsync(monitor, area, pending, cursor.IsChecked == true,
                smartZoom.IsChecked == true, audio, cameraSource, stop.Token);
            var path = Path.Combine(destination, $"Recording {DateTime.Now:yyyy-MM-dd HH-mm-ss-fff}.mp4");
            File.Move(pending, path);
            result.TrySetResult(path);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            ToolTipService.SetToolTip(status, ex.Message);
            if (stop.IsCancellationRequested) result.TrySetException(ex);
        }
        finally
        {
            if (pending is not null) File.Delete(pending);
            running = false;
            if (stop.IsCancellationRequested || result.Task.IsCompleted)
            {
                if (!closed) Close();
            }
            else
            {
                foreach (var option in options.Children.OfType<CheckBox>()) option.IsEnabled = true;
                options.Visibility = Visibility.Visible;
                start.Visibility = Visibility.Visible;
                start.IsEnabled = true;
                end.IsEnabled = false;
                Place(compact: false);
            }
        }
    }
}
