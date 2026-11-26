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
    private static readonly uint ActivateMessage = RegisterWindowMessage($"{App.InstanceName}.Activate");
    private readonly nint hwnd;
    private readonly Action<CaptureCommand> execute;
    private readonly Action<NativeWindow.Rect> showMenu;
    private bool modernNotifications;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid GuidItem;
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
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(
        in NotifyIconIdentifier identifier, out NativeWindow.Rect iconLocation);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    public ShellIntegration(nint hwnd, Action<CaptureCommand> execute, Action<NativeWindow.Rect> showMenu, string[] bindings)
    {
        this.hwnd = hwnd;
        this.execute = execute;
        this.showMenu = showMenu;
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
            Flags = 7 | 0x80, Callback = TrayMessage, Icon = icon, Tip = "REAL Capture X", Info = "", InfoTitle = ""
        };
        var successful = Shell_NotifyIcon(command, ref data);
        if (command == 0)
        {
            data.Version = 4;
            modernNotifications = successful && Shell_NotifyIcon(4, ref data);
        }
        return successful;
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
            var notification = (int)(lParam & 0xFFFF);
            if (modernNotifications ? notification is 0x0400 or 0x0401 : notification == 0x0202)
                execute(CaptureCommand.Toolbar);
            if (notification == 0x007B || (!modernNotifications && notification == 0x0205))
                showMenu(TrayAnchor());
            return 0;
        }
        if (message == taskbarCreated) TrayAvailable = UpdateTray(0);
        if (message is 0x001A or 0x031A) Appearance.Refresh();
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public static void ActivateExisting() => PostMessage(new nint(0xFFFF), ActivateMessage, 0, 0);

    private NativeWindow.Rect TrayAnchor()
    {
        var identifier = new NotifyIconIdentifier
        {
            Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = hwnd, Id = 1
        };
        if (Shell_NotifyIconGetRect(identifier, out var rect) == 0 && rect.Width > 0) return rect;

        NativeWindow.GetCursorPos(out var point);
        return new NativeWindow.Rect { Left = point.X, Right = point.X + 1, Top = point.Y, Bottom = point.Y + 1 };
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
