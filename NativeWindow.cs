using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace RealCaptureX;

internal static class NativeWindow
{
    internal const uint NoActivate = 0x0010;
    internal const int Hide = 0;
    internal const int ShowNoActivate = 4;
    internal static readonly nint Topmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool Contains(Point p) =>
            p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Bounds, Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrast
    {
        public uint Size, Flags;
        public nint Scheme;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint size, ref HighContrast data, uint options);

    internal static bool IsHighContrast()
    {
        var size = (uint)Marshal.SizeOf<HighContrast>();
        var status = new HighContrast { Size = size };
        return SystemParametersInfo(0x0042, size, ref status, 0) && (status.Flags & 1) != 0;
    }

    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint order, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect data, int bytes);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int data, int bytes);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int data, int bytes);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int length);

    internal static nint Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);
    internal static bool LeftPointerPressed() => (GetAsyncKeyState(1) & 0x8000) != 0;

    internal static void ExcludeFromCapture(Window window)
    {
        var applied = SetWindowDisplayAffinity(Handle(window), 0x11);
        Diagnostics.Log($"ExcludeFromCapture '{window.Title}' ok={applied} error={(applied ? 0 : Marshal.GetLastWin32Error())}");
    }

    /// <summary>App chrome never shows up in a capture; QA inspection builds keep it visible to test tools.</summary>
    internal static void ExcludeOverlayFromCapture(Window window)
    {
#if DEBUG
        if (QaMode.Inspect) return;
#endif
        ExcludeFromCapture(window);
    }

    internal static nint CursorMonitor()
    {
        GetCursorPos(out var cursor);
        return MonitorFromPoint(cursor, 2);
    }

    internal static nint WindowUnderCursor()
    {
        GetCursorPos(out var cursor);
        return GetAncestor(WindowFromPoint(cursor), 2);
    }

    internal static MonitorInfo GetMonitor(nint monitor)
    {
        var result = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref result))
            throw new InvalidOperationException("This display is no longer available.");
        return result;
    }

    internal static void ToolWindow(Window window, bool rounded = true)
    {
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        var showInSwitchers = false;
#if DEBUG
        showInSwitchers = QaMode.Inspect;
#endif
        window.AppWindow.IsShownInSwitchers = showInSwitchers;
        var hwnd = Handle(window);
        const int extendedStyle = -20;
        var style = GetWindowLongPtr(hwnd, extendedStyle).ToInt64();
        if (!showInSwitchers)
            SetWindowLongPtr(hwnd, extendedStyle, (nint)((style | 0x80) & ~0x40000L));
        DwmSetWindowAttribute(hwnd, 33, rounded ? 2 : 1, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint Region;
        public int Transition;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, in BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);

    /// <summary>
    /// Turns a borderless WinUI window into a per-pixel transparent popup: no DWM frame, border, corner
    /// clip or shadow (those caused the bright 1 px outline), host-backdrop brushes allowed, and an empty
    /// blur-behind region so DWM composes the client area with alpha.
    /// </summary>
    internal static void MakeTransparent(Window window)
    {
        var hwnd = Handle(window);
        const int styleIndex = -16;
        var style = GetWindowLongPtr(hwnd, styleIndex).ToInt64();
        style &= ~(0x00C00000L | 0x00040000L | 0x00080000L | 0x00800000L | 0x00400000L);
        style |= 0x80000000L;
        SetWindowLongPtr(hwnd, styleIndex, (nint)style);
        var results = new List<int>
        {
            DwmSetWindowAttribute(hwnd, 2, 2, sizeof(int)),
            DwmSetWindowAttribute(hwnd, 33, 1, sizeof(int)),
            DwmSetWindowAttribute(hwnd, 34, unchecked((int)0xFFFFFFFE), sizeof(int)),
            DwmSetWindowAttribute(hwnd, 17, 1, sizeof(int))
        };
        var region = CreateRectRgn(-2, -2, -1, -1);
        results.Add(DwmEnableBlurBehindWindow(hwnd, new BlurBehind { Flags = 3, Enable = 1, Region = region }));
        DeleteObject(region);
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        Diagnostics.Log($"MakeTransparent '{window.Title}' style=0x{style:X} hr=[{string.Join(",", results.Select(r => $"0x{r:X8}"))}]");
    }

    internal static void StyleFloat(Window window, bool dark)
    {
        const int useImmersiveDarkMode = 20;
        const int borderColor = 34;
        var hwnd = Handle(window);
        DwmSetWindowAttribute(hwnd, useImmersiveDarkMode, dark ? 1 : 0, sizeof(int));
        DwmSetWindowAttribute(hwnd, borderColor, unchecked((int)0xFFFFFFFE), sizeof(int));
    }

    /// <summary>
    /// Positions a window on a monitor's work area. <paramref name="glass"/> is the transparent shadow margin
    /// (DIPs) of glass windows: the visible panel, not the HWND, keeps the 16 px distance from the edge.
    /// </summary>
    internal static void Place(Window window, nint monitor, int width, int height, double anchorX, double anchorY, double glass = 0)
    {
        var work = GetMonitor(monitor).Work;
        var hwnd = Handle(window);
        if (MonitorFromWindow(hwnd, 2) != monitor)
            SetWindowPos(hwnd, 0, work.Left + work.Width / 2, work.Top + work.Height / 2, 1, 1, NoActivate | 0x0004);
        var scale = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
        var margin = (int)Math.Round(glass * scale);
        var w = Math.Min(work.Width + margin * 2, (int)Math.Round(width * scale));
        var h = Math.Min(work.Height + margin * 2, (int)Math.Round(height * scale));
        var padding = (int)Math.Round(16 * scale) - margin;
        var x = work.Left + (int)Math.Round((work.Width - w - padding * 2) * anchorX) + padding;
        var y = work.Top + (int)Math.Round((work.Height - h - padding * 2) * anchorY) + padding;
        window.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    /// <summary>
    /// Places a popup next to an anchor (physical pixels), preferring above it and right-aligned, clamped to
    /// the work area. Returns whether the popup opened above the anchor and the point of the panel (normalized)
    /// closest to the anchor's centre, so the popup can grow out of the control that opened it.
    /// </summary>
    internal static (bool Above, System.Numerics.Vector2 Origin) PlacePopup(Window window, Rect anchor, int width, int height, double glass = 0, bool alignLeft = false)
    {
        var center = new Point { X = anchor.Left + anchor.Width / 2, Y = anchor.Top + anchor.Height / 2 };
        var monitor = MonitorFromPoint(center, 2);
        var work = GetMonitor(monitor).Work;
        var hwnd = Handle(window);
        if (MonitorFromWindow(hwnd, 2) != monitor)
            SetWindowPos(hwnd, 0, center.X, center.Y, 1, 1, NoActivate | 0x0004);
        var scale = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
        var margin = (int)Math.Round(glass * scale);
        var gap = (int)Math.Round(8 * scale);
        var w = Math.Min((int)Math.Round(width * scale), work.Width - 2 * gap + 2 * margin);
        var h = Math.Min((int)Math.Round(height * scale), work.Height - 2 * gap + 2 * margin);
        var panelW = w - 2 * margin;
        var panelH = h - 2 * margin;
        var panelX = Math.Clamp(alignLeft ? anchor.Left : anchor.Right - panelW, work.Left + gap, work.Right - panelW - gap);
        var above = anchor.Top - panelH - gap;
        var below = anchor.Bottom + gap;
        var opensAbove = above >= work.Top + gap;
        var panelY = Math.Clamp(opensAbove ? above : below, work.Top + gap, work.Bottom - panelH - gap);
        window.AppWindow.MoveAndResize(new RectInt32(panelX - margin, panelY - margin, w, h));
        var originX = panelW > 0 ? Math.Clamp((center.X - panelX) / (float)panelW, 0f, 1f) : .5f;
        var originY = panelY >= anchor.Bottom - gap ? 0f : panelY + panelH <= anchor.Top + gap ? 1f : .5f;
        return (opensAbove, new System.Numerics.Vector2(originX, originY));
    }

    /// <summary>Screen rectangle (physical pixels) of an element inside a window.</summary>
    internal static Rect ScreenRect(Window window, FrameworkElement element)
    {
        var scale = Math.Max(96, GetDpiForWindow(Handle(window))) / 96.0;
        var origin = element.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var position = window.AppWindow.Position;
        var left = position.X + (int)Math.Round(origin.X * scale);
        var top = position.Y + (int)Math.Round(origin.Y * scale);
        return new Rect
        {
            Left = left, Top = top,
            Right = left + (int)Math.Round(element.ActualWidth * scale), Bottom = top + (int)Math.Round(element.ActualHeight * scale)
        };
    }

    internal sealed record Target(nint Handle, Rect Bounds, string Title);

    internal static IReadOnlyList<Target> ListTargets()
    {
        var targets = new List<Target>();
        var shell = GetShellWindow();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == shell || !IsWindowVisible(hwnd) || IsIconic(hwnd))
                return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId || DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            if (DwmGetWindowAttribute(hwnd, 9, out Rect bounds, Marshal.SizeOf<Rect>()) != 0)
                return true;
            var title = new System.Text.StringBuilder(256);
            if (GetWindowText(hwnd, title, title.Capacity) != 0 && bounds.Width > 0 && bounds.Height > 0)
                targets.Add(new Target(hwnd, bounds, title.ToString()));
            return true;
        }, 0);
        return targets;
    }
}
