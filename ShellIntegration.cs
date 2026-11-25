using System.Runtime.InteropServices;

namespace RealCaptureX;

internal enum CaptureCommand
{
    Region = 1, Window, Display, Toolbar, Recent, History, Settings, Exit, Text, Scroll, RecordRegion, RecordDisplay, OpenStudio,
    RepeatRegion, ScrollHorizontal
}

internal sealed class ShellIntegration : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private static readonly uint ActivateMessage = RegisterWindowMessage("RealCaptureX.Activate");
    private readonly nint hwnd;
    private readonly Action<CaptureCommand> execute;
    private readonly Subclass callback;
    private readonly uint taskbarCreated;
    private readonly nint icon;
    private readonly bool ownsIcon;
    private bool disposed;
    private string[] bindings;
    public List<string> UnavailableShortcuts { get; } = [];
    public bool TrayAvailable { get; private set; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    private delegate nint Subclass(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, Subclass proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, Subclass proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadIcon(nint instance, nint name);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint ExtractIcon(nint instance, string file, uint index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? content);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    public ShellIntegration(nint hwnd, Action<CaptureCommand> execute, string[] bindings)
    {
        this.hwnd = hwnd;
        this.execute = execute;
        this.bindings = bindings;
        callback = OnMessage;
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var extracted = ExtractIcon(0, Environment.ProcessPath!, 0);
        ownsIcon = extracted > 1;
        icon = ownsIcon ? extracted : LoadIcon(0, 32512);
        if (!SetWindowSubclass(hwnd, callback, 1, 0))
        {
            if (ownsIcon) DestroyIcon(icon);
            throw new InvalidOperationException("Windows could not initialize menu and keyboard shortcut handling.");
        }
        TrayAvailable = UpdateTray(0);

        RegisterBindings(CaptureShortcut.ParseAll(bindings));
    }

    private void RegisterBindings(CaptureShortcut[] parsed)
    {
        UnavailableShortcuts.Clear();
        for (var i = 0; i < parsed.Length; i++)
            if (!RegisterHotKey(hwnd, i + 1, parsed[i].Modifiers | 0x4000, parsed[i].Key))
                UnavailableShortcuts.Add(bindings[i]);
    }

    private void UnregisterBindings()
    {
        for (var i = 1; i <= 5; i++) UnregisterHotKey(hwnd, i);
    }

    public void ChangeShortcuts(string[] requested)
    {
        if (bindings.SequenceEqual(requested, StringComparer.OrdinalIgnoreCase)) return;
        var parsed = CaptureShortcut.ParseAll(requested);
        var old = bindings;
        UnregisterBindings();
        bindings = requested;
        RegisterBindings(parsed);
        if (UnavailableShortcuts.Count == 0) return;
        var failure = string.Join(", ", UnavailableShortcuts);
        UnregisterBindings();
        bindings = old;
        RegisterBindings(CaptureShortcut.ParseAll(old));
        throw new InvalidOperationException($"Shortcut already in use: {failure}");
    }

    private bool UpdateTray(uint command)
    {
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = 1,
            Flags = 7, Callback = TrayMessage, Icon = icon, Tip = "REAL Capture X", Info = "", InfoTitle = ""
        };
        return Shell_NotifyIcon(command, ref data);
    }

    private nint OnMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == ActivateMessage)
        {
            execute(CaptureCommand.Toolbar);
            return 0;
        }
        if (message == 0x0312 && wParam is >= 1 and <= 5)
        {
            execute((CaptureCommand)wParam);
            return 0;
        }
        if (message == TrayMessage)
        {
            if (lParam == 0x0202) execute(CaptureCommand.Toolbar);
            if (lParam is 0x0205 or 0x007B) ShowMenu();
            return 0;
        }
        if (message == taskbarCreated) TrayAvailable = UpdateTray(0);
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public static void ActivateExisting() => PostMessage(new nint(0xFFFF), ActivateMessage, 0, 0);

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        uint selected;
        try
        {
            Add(CaptureCommand.Region, $"Capture region\t{bindings[0]}");
            Add(CaptureCommand.Window, $"Capture window\t{bindings[1]}");
            Add(CaptureCommand.Display, $"Capture display\t{bindings[2]}");
            Add(CaptureCommand.RepeatRegion, "Repeat last region");
            Add(CaptureCommand.Text, "Capture text");
            Add(CaptureCommand.Scroll, "Scrolling capture");
            Add(CaptureCommand.ScrollHorizontal, "Horizontal scrolling capture");
            Add(CaptureCommand.RecordRegion, "Record region");
            Add(CaptureCommand.RecordDisplay, "Record display");
            AppendMenu(menu, 0x800, 0, null);
            Add(CaptureCommand.Toolbar, $"Capture toolbar\t{bindings[3]}");
            Add(CaptureCommand.Recent, $"Quick Access\t{bindings[4]}");
            Add(CaptureCommand.History, "Capture history");
            Add(CaptureCommand.OpenStudio, "Open video in Studio");
            Add(CaptureCommand.Settings, "Settings");
            AppendMenu(menu, 0x800, 0, null);
            Add(CaptureCommand.Exit, "Quit REAL Capture X");
            NativeWindow.GetCursorPos(out var cursor);
            NativeWindow.SetForegroundWindow(hwnd);
            selected = TrackPopupMenu(menu, 0x0102, cursor.X, cursor.Y, 0, hwnd, 0);
            PostMessage(hwnd, 0, 0, 0);
        }
        finally
        {
            DestroyMenu(menu);
        }
        if (selected != 0) execute((CaptureCommand)selected);
        void Add(CaptureCommand command, string name) => AppendMenu(menu, 0, (nuint)command, name);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        UpdateTray(2);
        UnregisterBindings();
        RemoveWindowSubclass(hwnd, callback, 1);
        if (ownsIcon) DestroyIcon(icon);
    }
}
