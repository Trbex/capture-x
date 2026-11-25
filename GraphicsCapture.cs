using Microsoft.Graphics.Canvas;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace RealCaptureX;

internal static class GraphicsCapture
{
    private static readonly Guid InteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid ItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint result);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint value);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);

    private static unsafe GraphicsCaptureItem CreateItem(nint handle, bool window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var hstring));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, InteropId, out var factory));
            try
            {
                var vtable = *(nint**)factory;
                var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)vtable[window ? 3 : 4];
                var iid = ItemId;
                nint item = 0;
                Marshal.ThrowExceptionForHR(create(factory, handle, &iid, &item));
                try
                {
                    return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
                }
                finally
                {
                    Marshal.Release(item);
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    public static Task<CaptureFrame> GrabAsync(nint handle, bool window, bool cursor = false) => Task.Run(async () =>
    {
        using var feed = new Feed(handle, window, cursor);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var frame = await feed.NextAsync(timeout.Token);
            return feed.Read(frame) with { HasAlpha = window };
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Windows could not read the selected source. Make sure it is visible and try again.");
        }
    });

    internal sealed class Feed : IDisposable
    {
        private readonly Direct3D11CaptureFramePool pool;
        private readonly GraphicsCaptureSession session;
        private SizeInt32 size;
        public CanvasDevice Device { get; }
        public SizeInt32 Size => size;

        public Feed(nint handle, bool window, bool cursor)
        {
            if (!GraphicsCaptureSession.IsSupported())
                throw new InvalidOperationException("Screen capture is unavailable on this display.");
            var item = CreateItem(handle, window);
            size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
                throw new InvalidOperationException("The selected source has no visible content.");
            Device = CanvasDevice.GetSharedDevice();
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(Device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            try
            {
                session = pool.CreateCaptureSession(item);
                try
                {
                    session.IsCursorCaptureEnabled = cursor;
                    session.StartCapture();
                }
                catch { session.Dispose(); throw; }
            }
            catch { pool.Dispose(); throw; }
        }

        public async Task<Direct3D11CaptureFrame> NextAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryNext() is { } frame) return frame;
                await Task.Delay(12, cancellationToken);
            }
        }

        public Direct3D11CaptureFrame? TryNext()
        {
            var frame = pool.TryGetNextFrame();
            if (frame is null) return null;
            var content = frame.ContentSize;
            if (content.Width > 0 && content.Height > 0 &&
                content.Width <= size.Width && content.Height <= size.Height)
                return frame;
            frame.Dispose();
            if (content.Width > size.Width || content.Height > size.Height)
            {
                pool.Recreate(Device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
                size = content;
            }
            return null;
        }

        public CaptureFrame Read(Direct3D11CaptureFrame frame, RectInt32? selection = null)
        {
            var content = frame.ContentSize;
            var rect = selection ?? new RectInt32(0, 0, content.Width, content.Height);
            if (rect.X < 0 || rect.Y < 0 || rect.Width > content.Width - rect.X || rect.Height > content.Height - rect.Y)
                throw new InvalidOperationException("The display changed during capture.");
            using var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(Device, frame.Surface);
            return new CaptureFrame(rect.Width, rect.Height, bitmap.GetPixelBytes(rect.X, rect.Y, rect.Width, rect.Height));
        }

        public void Dispose()
        {
            session.Dispose();
            pool.Dispose();
        }
    }
}
