using Microsoft.UI.Xaml;
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
    private readonly TrayMenuWindow tray;
    private readonly QuickAccessWindow quick;
    private ShellIntegration? shell;
    private SettingsWindow? settingsWindow;
    private HistoryWindow? historyWindow;
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
        tray = new TrayMenuWindow(Queue);
        toolbar = new ToolbarWindow(Queue, HideToolbar, ShowTrayMenu);
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
        Appearance.Initialize(toolbar.DispatcherQueue);
        string? status = null;
        try
        {
            settings = store.ReadSettings();
            Appearance.SetTheme(settings.Theme);
            captures = store.ReadAll();
            shell = new ShellIntegration(NativeWindow.Handle(toolbar), Queue, ShowTrayMenu, settings.KeyBindings);
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

    private void ShowTrayMenu(NativeWindow.Rect anchor) => toolbar.DispatcherQueue.TryEnqueue(() =>
    {
        if (!capturing && !exiting) tray.Show(anchor, settings.KeyBindings);
    });

    private async Task ExecuteAsync(CaptureCommand command)
    {
        if (capturing || exiting) return;
        if (tray.AppWindow.IsVisible) tray.HideNow();
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
        toolbar.Show();
    }

    private void HideToolbar()
    {
        if (shell is not null && (shell.TrayAvailable || shell.UnavailableShortcuts.Count < 5))
            _ = toolbar.AnimateHideAsync();
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
            toolbar.HideNow();
            quick.HideNow();
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
        toolbar.YieldToWorkspace();
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

    private void DismissOverlay() => _ = quick.DismissAsync();

    private async Task OpenAsync(CaptureEntry capture)
    {
        toolbar.YieldToWorkspace();
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

    private Task SaveAsync(CaptureEntry capture) => SaveAsync(capture, quick);

    private static async Task SaveAsync(CaptureEntry capture, Window owner)
    {
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
        toolbar.YieldToWorkspace();
        if (settingsWindow is not null)
        {
            settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(settings, updated =>
        {
            CaptureShortcut.ParseAll(updated.KeyBindings);
            shell?.ChangeShortcuts(updated.KeyBindings);
            try { store.SaveSettings(updated); }
            catch { shell?.ChangeShortcuts(settings.KeyBindings); throw; }
            settings = updated;
            Appearance.SetTheme(settings.Theme);
            toolbar.SetShortcuts(settings.KeyBindings, shell?.UnavailableShortcuts);
        });
        settingsWindow = window;
        window.Closed += (_, _) => settingsWindow = null;
        window.Activate();
    }

    private void ShowHistory()
    {
        toolbar.YieldToWorkspace();
        if (historyWindow is not null)
        {
            historyWindow.Refresh();
            historyWindow.Activate();
            return;
        }

        var window = new HistoryWindow(store, OpenAsync, OpenStudioAsync, CopyAsync,
            capture => SaveAsync(capture, (Window?)historyWindow ?? toolbar), PinAsync, () =>
            {
                captures = store.ReadAll();
                overlayIndex = Math.Min(overlayIndex, Math.Max(0, captures.Count - 1));
                quick.HideNow();
            });
        historyWindow = window;
        window.Closed += (_, _) => historyWindow = null;
        window.Activate();
    }

    private void Exit()
    {
        exiting = true;
        shell?.Dispose();
        tray.Close();
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
