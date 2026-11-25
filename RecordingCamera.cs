using Microsoft.Graphics.Canvas;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace RealCaptureX;

internal sealed class RecordingCamera(MediaCapture capture, MediaFrameReader reader) : IDisposable
{
    public static async Task<RecordingCamera> OpenAsync()
    {
        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly
            });
            var source = capture.FrameSources.Values.FirstOrDefault(candidate => candidate.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new InvalidOperationException("Windows could not find a color camera.");
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Argb32);
            try
            {
                reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                var status = await reader.StartAsync();
                if (status != MediaFrameReaderStartStatus.Success)
                    throw new InvalidOperationException($"Camera could not start: {status}.");
                return new RecordingCamera(capture, reader);
            }
            catch { reader.Dispose(); throw; }
        }
        catch { capture.Dispose(); throw; }
    }

    public CanvasBitmap? Latest(CanvasDevice device)
    {
        using var frame = reader.TryAcquireLatestFrame();
        using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null) return null;
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && bitmap.BitmapAlphaMode != BitmapAlphaMode.Straight)
            return CanvasBitmap.CreateFromSoftwareBitmap(device, bitmap);
        using var bgra = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        return CanvasBitmap.CreateFromSoftwareBitmap(device, bgra);
    }

    public void Dispose()
    {
        reader.Dispose();
        capture.Dispose();
    }
}
