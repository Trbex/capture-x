using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;

namespace RealCaptureX;

internal sealed class CaptureController
{
    private readonly CaptureStore store = new();
    private readonly ToolbarWindow toolbar;
    private readonly QuickAccessWindow quick;
    private ShellIntegration? shell;
    private Window? settingsWindow;
    private Window? historyWindow;
    private readonly Dictionary<string, EditorWindow> editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<PinnedWindow> pins = [];
    private readonly Dictionary<string, StudioWindow> studios = new(StringComparer.OrdinalIgnoreCase);
    private CaptureSettings settings = new();
    private IReadOnlyList<CaptureEntry> captures = [];
    private int overlayIndex;
    private nint overlayMonitor;
    private bool capturing;
    private bool exiting;
    private (nint Monitor, RectInt32 Area)? lastRegion;

    public CaptureController()
    {
        toolbar = new ToolbarWindow(Queue, HideToolbar);
        quick = new QuickAccessWindow(OpenAsync, CopyAsync, SaveAsync, PinAsync, RecognizeAsync,
            delta => _ = BrowseOverlayAsync(delta), DismissOverlay);
        toolbar.AppWindow.Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true;
            HideToolbar();
        };
        toolbar.Closed += (_, _) => shell?.Dispose();
    }

    public void Start()
    {
        string? status = null;
        try
        {
            settings = store.ReadSettings();
            captures = store.ReadAll();
            shell = new ShellIntegration(NativeWindow.Handle(toolbar), Queue, settings.KeyBindings);
            toolbar.SetShortcuts(settings.KeyBindings, shell.UnavailableShortcuts);
            if (!shell.TrayAvailable)
                status = $"System tray is unavailable. Use {settings.KeyBindings[3]}.";
        }
        catch (Exception ex)
        {
            status = ex.Message;
        }
        ShowToolbar(status);
    }

    private void Queue(CaptureCommand command) =>
        toolbar.DispatcherQueue.TryEnqueue(async () => await ExecuteAsync(command));

    private async Task ExecuteAsync(CaptureCommand command)
    {
        if (capturing || exiting) return;
        try
        {
            switch (command)
            {
                case CaptureCommand.Toolbar: ShowToolbar(); break;
                case CaptureCommand.Recent: await RecallOverlayAsync(); break;
                case CaptureCommand.History: ShowHistory(); break;
                case CaptureCommand.Settings: ShowSettings(); break;
                case CaptureCommand.OpenStudio: await PickVideoAsync(); break;
                case CaptureCommand.Exit: Exit(); break;
                default: await CaptureAsync(command); break;
            }
        }
        catch (Exception ex)
        {
            ShowToolbar(ex.Message);
        }
    }

    private void ShowToolbar(string? status = null)
    {
        toolbar.SetStatus(status);
        NativeWindow.Place(toolbar, NativeWindow.CursorMonitor(), 426, 112, 0.5, 0.86);
        toolbar.Activate();
    }

    private void HideToolbar()
    {
        if (shell is not null && (shell.TrayAvailable || shell.UnavailableShortcuts.Count < 5))
            toolbar.AppWindow.Hide();
        else
            toolbar.SetStatus("Capture toolbar is the active entry point.");
    }

    private async Task CaptureAsync(CaptureCommand command)
    {
        capturing = true;
        var wasVisible = toolbar.AppWindow.IsVisible;
        var previewWasVisible = quick.AppWindow.IsVisible;
        try
        {
            if (command == CaptureCommand.RepeatRegion && lastRegion is null)
                throw new InvalidOperationException("Capture a region before repeating it.");
            var monitor = command == CaptureCommand.RepeatRegion ? lastRegion!.Value.Monitor : NativeWindow.CursorMonitor();
            var bounds = NativeWindow.GetMonitor(monitor).Bounds;
            toolbar.AppWindow.Hide();
            DismissOverlay();
            await Task.Run(NativeWindow.DwmFlush);
            if (command == CaptureCommand.RecordDisplay)
            {
                await RecordAsync(monitor, new RectInt32(0, 0, bounds.Width, bounds.Height), wasVisible);
                return;
            }
            var desktop = await GraphicsCapture.GrabAsync(monitor, window: false,
                cursor: settings.IncludePointer && command is CaptureCommand.Region or CaptureCommand.Display or CaptureCommand.RepeatRegion);
            var frame = desktop;
            if (command == CaptureCommand.RepeatRegion)
            {
                var previous = lastRegion!.Value.Area;
                if (previous.X + previous.Width > desktop.Width || previous.Y + previous.Height > desktop.Height)
                    throw new InvalidOperationException("The display changed. Select a new region.");
                frame = desktop.Crop(previous);
                command = CaptureCommand.Region;
            }
            else if (command is CaptureCommand.Region or CaptureCommand.Window or CaptureCommand.Text or CaptureCommand.Scroll
                or CaptureCommand.ScrollHorizontal or CaptureCommand.RecordRegion)
            {
                IReadOnlyList<NativeWindow.Target>? targets = command == CaptureCommand.Window ? NativeWindow.ListTargets() : null;
                if (targets is { Count: 0 })
                    throw new InvalidOperationException("Open a visible window, then try window capture again.");
                var selection = new SelectionWindow(desktop, bounds, targets);
                var picked = await selection.PickAsync();
                if (picked is null)
                {
                    if (wasVisible) ShowToolbar();
                    if (previewWasVisible) await ShowOverlayAsync(animate: false);
                    return;
                }
                if (command == CaptureCommand.RecordRegion)
                {
                    if (picked.Area.Width < 64 || picked.Area.Height < 64)
                        throw new InvalidOperationException("Select a recording region of at least 64 × 64 pixels.");
                    await RecordAsync(monitor, picked.Area, wasVisible);
                    return;
                }
                if (picked.Window == 0)
                    frame = desktop.Crop(picked.Area);
                else
                {
                    await Task.Run(NativeWindow.DwmFlush);
                    await Task.Delay(45);
                    frame = await GraphicsCapture.GrabAsync(picked.Window, window: true, cursor: settings.IncludePointer);
                }
                if (command == CaptureCommand.Region) lastRegion = (monitor, picked.Area);
                if (command is CaptureCommand.Scroll or CaptureCommand.ScrollHorizontal)
                {
                    var stitched = await new ScrollWindow(command == CaptureCommand.ScrollHorizontal).RunAsync(monitor, picked.Area, frame);
                    if (stitched is null)
                    {
                        if (wasVisible) ShowToolbar();
                        return;
                    }
                    frame = stitched;
                }
            }

            if (command == CaptureCommand.Text)
            {
                using var png = await frame.EncodeAsync();
                using var read = png.AsStreamForRead();
                using var bytes = new MemoryStream();
                await read.CopyToAsync(bytes);
                var recognized = await TextRecognition.CopyAsync(bytes.ToArray());
                if (recognized.Length == 0) ShowToolbar("No text detected");
                else if (wasVisible) ShowToolbar("Text copied to clipboard");
                return;
            }

            var entry = await store.AddAsync(frame, command.ToString());
            captures = new[] { entry }.Concat(captures).ToArray();
            overlayIndex = 0;
            overlayMonitor = monitor;
            if (settings.ShowPreview) await ShowOverlayAsync(animate: true);
            if (settings.AutoCopy) await CopyAsync(entry);
            if (!settings.ShowPreview && wasVisible) ShowToolbar("Capture saved to history");
        }
        finally
        {
            capturing = false;
        }
    }

    private async Task RecallOverlayAsync()
    {
        captures = store.ReadAll();
        if (captures.Count == 0)
        {
            ShowToolbar("Take a screenshot to use Quick Access.");
            return;
        }
        overlayIndex = 0;
        overlayMonitor = NativeWindow.CursorMonitor();
        await ShowOverlayAsync(animate: true);
    }

    private async Task RecordAsync(nint monitor, RectInt32 area, bool toolbarWasVisible)
    {
        var path = await new RecordingWindow(monitor, area).ShowAsync();
        if (path is not null) await OpenStudioAsync(path);
        else if (toolbarWasVisible) ShowToolbar();
    }

    private async Task PickVideoAsync()
    {
        if (!toolbar.AppWindow.IsVisible) ShowToolbar();
        var folder = Path.Combine(CaptureStore.Root, "Recordings");
        Directory.CreateDirectory(folder);
        var picker = new FileOpenPicker(toolbar.AppWindow.Id) { SuggestedFolder = folder };
        picker.FileTypeFilter.Add(".mp4");
        var picked = await picker.PickSingleFileAsync();
        if (picked is not null) await OpenStudioAsync(picked.Path);
    }

    private async Task OpenStudioAsync(string path)
    {
        if (studios.TryGetValue(path, out var existing))
        {
            existing.Activate();
            return;
        }
        var window = new StudioWindow(path);
        studios.Add(path, window);
        window.Closed += (_, _) => studios.Remove(path);
        window.Activate();
        try { await window.LoadAsync(); }
        catch { window.Close(); throw; }
    }

    private async Task BrowseOverlayAsync(int delta)
    {
        try
        {
            overlayIndex = Math.Clamp(overlayIndex + delta, 0, Math.Max(0, captures.Count - 1));
            await ShowOverlayAsync(animate: false);
        }
        catch (Exception ex)
        {
            ShowToolbar(ex.Message);
        }
    }

    private Task ShowOverlayAsync(bool animate)
    {
        if (captures.Count == 0) return Task.CompletedTask;
        var corner = settings.Corner;
        return quick.DisplayAsync(captures[overlayIndex], overlayMonitor, overlayIndex, captures.Count,
            settings.Timeout, corner % 2, corner < 2 ? 1 : 0, animate);
    }

    private void DismissOverlay() => quick.Suspend();

    private async Task OpenAsync(CaptureEntry capture)
    {
        if (editors.TryGetValue(capture.Path, out var existing))
        {
            existing.Activate();
            return;
        }
        var editor = new EditorWindow(capture, async frame =>
        {
            var path = await PickExportPathAsync(editors[capture.Path], capture.Created, "Edited screenshot");
            if (path is null) return;
            if (string.Equals(path, capture.Path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different file name to preserve the original screenshot.");
            await CaptureStore.WritePngAsync(path, frame, replace: true);
            await store.AddAsync(frame, "Edited");
            captures = store.ReadAll();
        });
        editors.Add(capture.Path, editor);
        editor.Closed += (_, _) => editors.Remove(capture.Path);
        editor.Activate();
        try { await editor.LoadAsync(); }
        catch { editor.Close(); throw; }
    }

    private static async Task CopyAsync(CaptureEntry capture)
    {
        var file = await StorageFile.GetFileFromPathAsync(capture.Path);
        var data = new DataPackage();
        data.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        Clipboard.SetContent(data);
        Clipboard.Flush();
    }

    private async Task PinAsync(CaptureEntry capture)
    {
        var pin = new PinnedWindow(capture);
        pins.Add(pin);
        pin.Closed += (_, _) => pins.Remove(pin);
        try { await pin.ShowAsync(); }
        catch { pin.Close(); throw; }
    }

    private static async Task RecognizeAsync(CaptureEntry capture)
    {
        var text = await TextRecognition.CopyAsync(await File.ReadAllBytesAsync(capture.Path));
        if (text.Length == 0) throw new InvalidOperationException("No text detected in this screenshot.");
    }

    private async Task SaveAsync(CaptureEntry capture)
    {
        var owner = quick.AppWindow.IsVisible ? quick : historyWindow ?? toolbar;
        var path = await PickExportPathAsync(owner, capture.Created, "Screenshot");
        if (path is null || string.Equals(capture.Path, path, StringComparison.OrdinalIgnoreCase)) return;
        await Task.Run(() =>
        {
            var pending = Path.Combine(Path.GetDirectoryName(path)!, $".realcapture-{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(capture.Path, pending);
                File.Move(pending, path, overwrite: true);
            }
            finally { File.Delete(pending); }
        });
    }

    private static async Task<string?> PickExportPathAsync(Window owner, DateTimeOffset created, string label)
    {
        var picker = new FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedFileName = $"{label} {created.ToLocalTime():yyyy-MM-dd HH-mm-ss}",
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            DefaultFileExtension = ".png"
        };
#if DEBUG
        if (Environment.GetEnvironmentVariable("REAL_CAPTURE_X_INSPECT") == "1")
        {
            picker.SuggestedFolder = Path.Combine(CaptureStore.Root, "Exports");
            Directory.CreateDirectory(picker.SuggestedFolder);
        }
#endif
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    private void ShowSettings()
    {
        if (settingsWindow is not null)
        {
            settingsWindow.Activate();
            return;
        }
        var window = CreatePanel("Settings", 438, 670, out var body);
        settingsWindow = window;
        window.Closed += (_, _) => settingsWindow = null;
        var visibility = new ToggleSwitch { Header = "Show Quick Access after capture", IsOn = settings.ShowPreview };
        var autoCopy = new ToggleSwitch { Header = "Copy screenshots automatically", IsOn = settings.AutoCopy };
        var cursor = new ToggleSwitch { Header = "Include pointer in screenshots", IsOn = settings.IncludePointer };
        var corner = new ComboBox
        {
            Header = "Preview position", ItemsSource = new[] { "Bottom left", "Bottom right", "Top left", "Top right" },
            SelectedIndex = settings.Corner, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var timeout = new NumberBox
        {
            Header = "Dismiss after seconds (0 keeps it visible)", Value = settings.Timeout, Minimum = 0, Maximum = 600,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        body.Children.Add(visibility);
        body.Children.Add(autoCopy);
        body.Children.Add(cursor);
        body.Children.Add(corner);
        body.Children.Add(timeout);
        var shortcutLabels = new[] { "Capture region", "Capture window", "Capture display", "Open toolbar", "Restore Quick Access" };
        var keys = new TextBox[shortcutLabels.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = new TextBox { Header = shortcutLabels[i], Text = settings.KeyBindings[i], MaxLength = 32 };
            body.Children.Add(keys[i]);
        }
        var status = Appearance.Label("", 12, Appearance.Muted);
        var apply = new Button { Content = "Save settings" };
        apply.Click += (_, _) =>
        {
            try
            {
                var bindings = keys.Select(key => key.Text.Trim()).ToArray();
                CaptureShortcut.ParseAll(bindings);
                var updated = new CaptureSettings(corner.SelectedIndex >= 0 ? corner.SelectedIndex : 0,
                    double.IsFinite(timeout.Value) ? Math.Clamp((int)timeout.Value, 0, 600) : settings.Timeout,
                    visibility.IsOn, autoCopy.IsOn, cursor.IsOn, bindings);
                shell?.ChangeShortcuts(bindings);
                try { store.SaveSettings(updated); }
                catch { shell?.ChangeShortcuts(settings.KeyBindings); throw; }
                settings = updated;
                toolbar.SetShortcuts(settings.KeyBindings, shell?.UnavailableShortcuts);
                status.Text = "Settings saved";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        body.Children.Add(apply);
        body.Children.Add(status);
        window.Activate();
    }

    private void ShowHistory()
    {
        if (historyWindow is not null)
        {
            historyWindow.Activate();
            return;
        }
        var window = CreatePanel("Capture history", 486, 470, out var body);
        historyWindow = window;
        window.Closed += (_, _) => historyWindow = null;
        var type = new ComboBox { ItemsSource = new[] { "Screenshots", "Recordings" }, SelectedIndex = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(type, "Capture type");
        body.Children.Add(type);
        var list = new ListView { Height = 250, IsItemClickEnabled = true };
        var status = Appearance.Label("", 12, Appearance.Muted);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        var videoActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        void Refresh()
        {
            var video = type.SelectedIndex == 1;
            actions.Visibility = video ? Visibility.Collapsed : Visibility.Visible;
            videoActions.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
            if (video)
            {
                var location = Path.Combine(CaptureStore.Root, "Recordings");
                var paths = Directory.Exists(location) ? Directory.GetFiles(location, "*.mp4").OrderDescending().ToArray() : [];
                list.ItemsSource = paths.Select(path => new ListViewItem { Tag = path, Content = Path.GetFileName(path) }).ToArray();
                status.Text = paths.Length == 0 ? "No recordings yet" : $"{paths.Length} recordings";
                return;
            }
            captures = store.ReadAll();
            overlayIndex = Math.Min(overlayIndex, Math.Max(0, captures.Count - 1));
            list.ItemsSource = captures.Select(entry => new ListViewItem
            {
                Content = $"{entry.Created.ToLocalTime():g}   ·   {entry.Mode}   ·   {entry.Width} × {entry.Height}",
                Tag = entry
            }).ToArray();
            status.Text = captures.Count == 0 ? "No screenshots yet" : $"{captures.Count} screenshots";
        }
        type.SelectionChanged += (_, _) => Refresh();
        list.DoubleTapped += async (_, _) =>
        {
            try
            {
                var entry = (list.SelectedItem as ListViewItem)?.Tag;
                if (entry is CaptureEntry screenshot) await OpenAsync(screenshot);
                if (entry is string video) await OpenStudioAsync(video);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        Refresh();
        body.Children.Add(list);
        AddAction("Open", OpenAsync);
        AddAction("Copy", CopyAsync);
        AddAction("Save as", SaveAsync);
        AddAction("Pin", PinAsync);
        AddAction("Delete", async entry =>
        {
            var prompt = new ContentDialog
            {
                XamlRoot = body.XamlRoot, Title = "Delete screenshot?", Content = "The saved capture will be moved to the Recycle Bin.",
                PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
            };
            if (await prompt.ShowAsync() != ContentDialogResult.Primary) return;
            await (await StorageFile.GetFileFromPathAsync(entry.Path)).DeleteAsync();
            DismissOverlay();
            Refresh();
        });
        body.Children.Add(actions);
        var openVideo = new Button { Content = "Open in Studio" };
        openVideo.Click += async (_, _) =>
        {
            if ((list.SelectedItem as ListViewItem)?.Tag is not string path) return;
            try { await OpenStudioAsync(path); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        videoActions.Children.Add(openVideo);
        body.Children.Add(videoActions);
        body.Children.Add(status);
        window.Activate();

        void AddAction(string name, Func<CaptureEntry, Task> action)
        {
            var button = new Button { Content = name };
            button.Click += async (_, _) =>
            {
                if ((list.SelectedItem as ListViewItem)?.Tag is not CaptureEntry entry) return;
                try { await action(entry); }
                catch (Exception ex) { status.Text = ex.Message; }
            };
            actions.Children.Add(button);
        }
    }

    private static Window CreatePanel(string title, int width, int height, out StackPanel body)
    {
        var window = new Window { Title = $"REAL Capture X — {title}" };
        Appearance.StyleTitleBar(window);
        body = new StackPanel
        {
            RequestedTheme = ElementTheme.Dark, Background = Appearance.Surface,
            Padding = new Thickness(18), Spacing = 10
        };
        var heading = Appearance.Label(title, 21);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        body.Children.Add(heading);
        window.Content = new ScrollViewer { Content = body, Background = Appearance.Surface };
        NativeWindow.Place(window, NativeWindow.CursorMonitor(), width, height, 0.5, 0.5);
        return window;
    }

    private void Exit()
    {
        exiting = true;
        shell?.Dispose();
        settingsWindow?.Close();
        historyWindow?.Close();
        foreach (var editor in editors.Values.ToArray()) editor.Close();
        foreach (var studio in studios.Values.ToArray()) studio.Close();
        foreach (var pin in pins.ToArray()) pin.Close();
        quick.Close();
        toolbar.Close();
        Application.Current.Exit();
    }
}
