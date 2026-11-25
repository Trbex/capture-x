using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace RealCaptureX;

internal sealed class RecordingWindow : Window
{
    private readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stop = new();
    private readonly TextBlock status = Appearance.Label("", 12, Appearance.Muted);
    private readonly Grid options = new() { ColumnSpacing = 8 };
    private readonly Border optionsCard;
    private readonly DispatcherQueueTimer timer;
    private readonly Stopwatch elapsed = new();
    private readonly TextBlock heading = Appearance.Label("Record screen", 14, strong: true);
    private readonly CheckBox cursor = new() { Content = "Show pointer", IsChecked = true };
    private readonly CheckBox system = new() { Content = "System audio" };
    private readonly CheckBox microphone = new() { Content = "Microphone" };
    private readonly CheckBox camera = new() { Content = "Webcam inset" };
    private readonly CheckBox smartZoom = new() { Content = "Smart zoom on clicks" };
    private readonly Button start = Appearance.Action("Start recording", icon: Glyph.Video, tone: ButtonTone.Primary);
    private readonly Button end = Appearance.Action("Stop recording", icon: Glyph.Stop, tone: ButtonTone.Raised);
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
        var root = new StackPanel { Padding = new Thickness(11), Spacing = 9 };
        var header = new Grid { ColumnSpacing = 9 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Appearance.Badge(Glyph.Video, 34));
        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(heading);
        status.Text = $"{area.Width} × {area.Height} px   ·   MP4";
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Children.Add(status);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var close = Appearance.IconButton(Glyph.Close, "Close recording controls", () =>
        {
            if (running) RequestStop(); else Close();
        });
        close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        root.Children.Add(header);

        options.ColumnDefinitions.Add(new ColumnDefinition());
        options.ColumnDefinitions.Add(new ColumnDefinition());
        for (var index = 0; index < 3; index++)
            options.RowDefinitions.Add(new RowDefinition { Height = new GridLength(39) });
        var choices = new[]
        {
            (cursor, Glyph.Cursor, "Show pointer"), (system, Glyph.Audio, "System audio"),
            (microphone, Glyph.Mic, "Microphone"), (camera, Glyph.Video, "Webcam inset"),
            (smartZoom, Glyph.Zoom, "Smart zoom on clicks")
        };
        for (var index = 0; index < choices.Length; index++)
        {
            var (option, icon, name) = choices[index];
            option.Content = Appearance.ActionContent(name, icon, Appearance.Text);
            option.MinWidth = 0;
            option.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(option, name);
            Grid.SetRow(option, index / 2);
            Grid.SetColumn(option, index % 2);
            if (index == choices.Length - 1) Grid.SetColumnSpan(option, 2);
            options.Children.Add(option);
        }
        optionsCard = new Border
        {
            Background = Appearance.Panel, BorderBrush = Appearance.Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 8, 4), Child = options
        };
        root.Children.Add(optionsCard);
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Children.Add(start);
        root.Children.Add(end);
        end.HorizontalAlignment = HorizontalAlignment.Stretch;
        end.Visibility = Visibility.Collapsed;
        Appearance.SetContent(this, Appearance.Frame(root), floating: true);

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => status.Text = $"MP4   ·   {elapsed.Elapsed:hh\\:mm\\:ss}";
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
            timer.Stop();
            result.TrySetResult(null);
            stop.Dispose();
        };
    }

    public Task<string?> ShowAsync()
    {
        Place(compact: false);
        ProductMotion.Enter((FrameworkElement)Content);
        Activate();
        return result.Task;
    }

    private void Place(bool compact) => NativeWindow.Place(this, monitor, compact ? 276 : 365, compact ? 103 : 238, 1, 0);

    private void RequestStop()
    {
        if (stop.IsCancellationRequested) return;
        timer.Stop();
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
            optionsCard.Visibility = Visibility.Collapsed;
            start.Visibility = Visibility.Collapsed;
            end.Visibility = Visibility.Visible;
            end.IsEnabled = true;
            heading.Text = "Recording";
            status.Text = "MP4   ·   00:00:00";
            elapsed.Restart();
            timer.Start();
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
            timer.Stop();
            if (pending is not null) File.Delete(pending);
            running = false;
            if (stop.IsCancellationRequested || result.Task.IsCompleted)
            {
                if (!closed) Close();
            }
            else
            {
                foreach (var option in options.Children.OfType<CheckBox>()) option.IsEnabled = true;
                optionsCard.Visibility = Visibility.Visible;
                start.Visibility = Visibility.Visible;
                start.IsEnabled = true;
                end.Visibility = Visibility.Collapsed;
                end.IsEnabled = false;
                heading.Text = "Record screen";
                Place(compact: false);
            }
        }
    }
}
