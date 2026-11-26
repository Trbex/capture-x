using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Hosting;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace RealCaptureX;

internal sealed class RecordingWindow : Window
{
    private const int SetupWidth = 380;
    private const int SetupHeight = 232;
    private const int CapsuleWidth = 268;
    private const int CapsuleHeight = 56;
    private readonly TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stop = new();
    private readonly TextBlock status = Appearance.Label("", 12, Appearance.Muted);
    private readonly TextBlock clock = Appearance.Label("00:00", 15, strong: true);
    private readonly StackPanel setup = new() { Padding = new Thickness(14, 12, 14, 14), Spacing = 12 };
    private readonly Grid capsule = new() { Padding = new Thickness(16, 0, 9, 0), ColumnSpacing = 10, Visibility = Visibility.Collapsed };
    private readonly Grid options = new() { ColumnSpacing = 6, RowSpacing = 6 };
    private readonly Border dot = Dot();
    private readonly DispatcherQueueTimer timer;
    private readonly Stopwatch elapsed = new();
    private readonly Dictionary<string, (Button Chip, bool On)> choices = [];
    private readonly Button start = Appearance.Action("Start recording", tone: ButtonTone.Primary);
    private readonly Button end = Appearance.Action("Stop", icon: Glyph.Stop, tone: ButtonTone.Raised);
    private readonly GlassSurface glass;
    private readonly nint monitor;
    private readonly RectInt32 area;
    private bool running;
    private bool closed;

    private static Border Dot() => new()
    {
        Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Appearance.Brush(255, 69, 58),
        VerticalAlignment = VerticalAlignment.Center
    };

    public RecordingWindow(nint monitor, RectInt32 area)
    {
        this.monitor = monitor;
        this.area = area;
        Title = "REAL Capture X — Recording";
        NativeWindow.ToolWindow(this);

        var header = new Grid { ColumnSpacing = 11 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Appearance.Badge(Glyph.Video, 34));
        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Appearance.Label("Record screen", 14, strong: true));
        status.Text = $"{area.Width} × {area.Height} px  ·  MP4";
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Children.Add(status);
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var close = Appearance.IconButton(Glyph.Close, "Close recording controls", () =>
        {
            if (running) RequestStop(); else _ = CloseAnimatedAsync();
        }, 30);
        close.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        setup.Children.Add(header);

        options.ColumnDefinitions.Add(new ColumnDefinition());
        options.ColumnDefinitions.Add(new ColumnDefinition());
        var chips = new (string Name, Glyph Icon, bool On)[]
        {
            ("Show pointer", Glyph.Cursor, true), ("System audio", Glyph.Audio, false),
            ("Microphone", Glyph.Mic, false), ("Webcam inset", Glyph.Video, false),
            ("Smart zoom on clicks", Glyph.Zoom, false)
        };
        for (var index = 0; index < chips.Length; index++)
        {
            var (name, icon, on) = chips[index];
            options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var chip = Appearance.Action(name, icon: icon, tone: ButtonTone.Raised);
            chip.HorizontalAlignment = HorizontalAlignment.Stretch;
            chip.HorizontalContentAlignment = HorizontalAlignment.Left;
            chip.Height = 36;
            chip.Padding = new Thickness(11, 0, 11, 0);
            chip.CornerRadius = new CornerRadius(10);
            choices[name] = (chip, on);
            Paint(chip, on);
            chip.Click += (_, _) => Toggle(name);
            Grid.SetRow(chip, index / 2);
            Grid.SetColumn(chip, index % 2);
            if (index == chips.Length - 1) Grid.SetColumnSpan(chip, 2);
            options.Children.Add(chip);
        }
        setup.Children.Add(options);
        var go = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        go.Children.Add(Dot());
        go.Children.Add(Appearance.Label("Start recording", 13, Appearance.OnAccent, strong: true));
        start.Content = go;
        start.Height = 38;
        start.CornerRadius = new CornerRadius(10);
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        setup.Children.Add(start);

        capsule.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        capsule.ColumnDefinitions.Add(new ColumnDefinition());
        capsule.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        capsule.Children.Add(dot);
        var live = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        live.Children.Add(clock);
        live.Children.Add(Appearance.Label($"{area.Width} × {area.Height}", 11, Appearance.Muted));
        Grid.SetColumn(live, 1);
        capsule.Children.Add(live);
        end.Height = 36;
        end.CornerRadius = new CornerRadius(18);
        end.Padding = new Thickness(13, 0, 15, 0);
        end.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(end, 2);
        capsule.Children.Add(end);

        var body = new Grid();
        body.Children.Add(setup);
        body.Children.Add(capsule);
        glass = Appearance.Float(this, body, radius: 16);

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => clock.Text = elapsed.Elapsed.TotalHours >= 1
            ? elapsed.Elapsed.ToString(@"h\:mm\:ss") : elapsed.Elapsed.ToString(@"mm\:ss");
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

    private bool On(string name) => choices[name].On;

    private void Toggle(string name)
    {
        if (running) return;
        var (chip, on) = choices[name];
        on = !on;
        choices[name] = (chip, on);
        Paint(chip, on);
    }

    /// <summary>On: brighter fill with a visible rim and full-strength label; off: quiet tint with muted label.</summary>
    private static void Paint(Button chip, bool on)
    {
        Appearance.SetTone(chip, on ? ButtonTone.Selected : ButtonTone.Raised);
        if (on)
        {
            chip.BorderBrush = Appearance.Muted;
            chip.BorderThickness = new Thickness(1);
        }
        chip.Opacity = on ? 1 : .72;
        AutomationProperties.SetItemStatus(chip, on ? "On" : "Off");
    }

    public Task<string?> ShowAsync()
    {
        Place(compact: false);
        Activate();
        glass.Enter(rise: -10);
        return result.Task;
    }

    private async Task CloseAnimatedAsync()
    {
        await glass.ExitAsync(drop: -8);
        if (!closed) Close();
    }

    private void Place(bool compact) => NativeWindow.Place(this, monitor,
        GlassSurface.Outer(compact ? CapsuleWidth : SetupWidth), GlassSurface.Outer(compact ? CapsuleHeight : SetupHeight),
        compact ? .5 : 1, 0, GlassSurface.Margin);

    private void RequestStop()
    {
        if (stop.IsCancellationRequested) return;
        timer.Stop();
        end.IsEnabled = false;
        clock.Text = "Saving…";
        stop.Cancel();
    }

    /// <summary>Endless soft pulse of the red recording dot (composition thread, no UI work).</summary>
    private void Pulse(bool on)
    {
        var visual = ElementCompositionPreview.GetElementVisual(dot);
        if (!on || !ProductMotion.Enabled)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
            return;
        }
        var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0, 1);
        pulse.InsertKeyFrame(.5f, .35f, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(.6f, 1)));
        pulse.InsertKeyFrame(1, 1, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(.6f, 1)));
        pulse.Duration = TimeSpan.FromMilliseconds(1600);
        pulse.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", pulse);
    }

    private void ShowCapsule(bool compact)
    {
        setup.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        capsule.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        glass.Panel.CornerRadius = new CornerRadius(compact ? CapsuleHeight / 2.0 : 16);
        glass.Backdrop.SetRadius(compact ? CapsuleHeight / 2.0 : 16);
        Place(compact);
        glass.Enter(rise: -8);
        Pulse(compact);
    }

    private async Task StartAsync()
    {
        if (running) return;
        running = true;
        string? pending = null;
        var destination = Path.Combine(CaptureStore.Root, "Recordings");
        start.IsEnabled = false;
        options.IsHitTestVisible = false;
        try
        {
            Directory.CreateDirectory(destination);
            pending = Path.Combine(destination, $".realcapture-{Guid.NewGuid():N}.tmp");
            using (File.Create(pending)) { }
            status.Text = "Preparing capture…";
            using var cameraSource = On("Webcam inset") ? await RecordingCamera.OpenAsync() : null;
            using var audio = On("System audio") || On("Microphone")
                ? new RecordingAudio(On("System audio"), On("Microphone")) : null;
            if (stop.IsCancellationRequested) return;
            end.IsEnabled = true;
            clock.Text = "00:00";
            elapsed.Restart();
            timer.Start();
            ShowCapsule(compact: true);
#if DEBUG
            if (!QaMode.Inspect)
#endif
                NativeWindow.ExcludeFromCapture(this);
            await ScreenRecorder.RecordAsync(monitor, area, pending, On("Show pointer"),
                On("Smart zoom on clicks"), audio, cameraSource, stop.Token);
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
            Pulse(false);
            if (pending is not null) File.Delete(pending);
            running = false;
            if (stop.IsCancellationRequested || result.Task.IsCompleted)
            {
                if (!closed) Close();
            }
            else
            {
                options.IsHitTestVisible = true;
                start.IsEnabled = true;
                end.IsEnabled = false;
                ShowCapsule(compact: false);
            }
        }
    }
}
