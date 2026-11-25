using Microsoft.Graphics.Canvas;
using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace RealCaptureX;

internal static class ScreenRecorder
{
    private const int Fps = 30;

    public static Task RecordAsync(nint monitor, RectInt32 area, string path, bool cursor, bool smartZoom,
        RecordingAudio? audio, RecordingCamera? camera, CancellationToken stop) => Task.Run(async () =>
    {
        using var feed = new GraphicsCapture.Feed(monitor, window: false, cursor);
        var bounds = NativeWindow.GetMonitor(monitor).Bounds;
        var ratio = Math.Min(1, Math.Min(3840.0 / area.Width, 2160.0 / area.Height));
        var width = Math.Max(2, (int)(area.Width * ratio) & ~1);
        var height = Math.Max(2, (int)(area.Height * ratio) & ~1);
        using var latest = new CanvasRenderTarget(feed.Device, width, height, 96);
        using (var startup = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(startup.Token, stop))
        using (var first = await feed.NextAsync(linked.Token))
            UpdateDesktop(first);

        var video = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)width, (uint)height);
        video.FrameRate.Numerator = Fps;
        video.FrameRate.Denominator = 1;
        var graphicsTrack = new VideoStreamDescriptor(video);
        var stream = audio is null
            ? new MediaStreamSource(graphicsTrack)
            : new MediaStreamSource(graphicsTrack,
                new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(RecordingAudio.Rate, RecordingAudio.Channels, 16)));
        stream.BufferTime = TimeSpan.Zero;
        stream.CanSeek = false;
        var clock = new Stopwatch();
        var videoIndex = 0L;
        var audioIndex = 0L;
        var lastClick = TimeSpan.MinValue;
        var pressed = false;
        var focus = new Point(width / 2.0, height / 2.0);
        Exception? sampleError = null;
        CanvasBitmap? cameraBitmap = null;
        var outstanding = new ConcurrentDictionary<CanvasRenderTarget, byte>();

        stream.Starting += (_, args) =>
        {
            audio?.StartTimeline();
            if (!clock.IsRunning) clock.Start();
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        };
        stream.SampleRequested += async (sender, args) =>
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            try
            {
                if (stop.IsCancellationRequested)
                {
                    request.Sample = null;
                    return;
                }
                if (!clock.IsRunning) clock.Start();
                if (request.StreamDescriptor is AudioStreamDescriptor)
                {
                    var audioTime = TimeSpan.FromMilliseconds(audioIndex++ * 20);
                    await WaitAsync(audioTime);
                    var sample = MediaStreamSample.CreateFromBuffer(audio!.ReadPacket(), audioTime);
                    sample.Duration = TimeSpan.FromMilliseconds(20);
                    request.Sample = sample;
                    return;
                }

                var timestamp = TimeSpan.FromTicks(videoIndex++ * TimeSpan.TicksPerSecond / Fps);
                await WaitAsync(timestamp);
                using (var frame = feed.TryNext())
                    if (frame is not null) UpdateDesktop(frame);

                if (camera?.Latest(feed.Device) is { } recent)
                {
                    cameraBitmap?.Dispose();
                    cameraBitmap = recent;
                }
                var surface = new CanvasRenderTarget(feed.Device, width, height, 96);
                try
                {
                    using (var drawing = surface.CreateDrawingSession())
                    {
                        drawing.Clear(Microsoft.UI.Colors.Black);
                        var crop = smartZoom ? ZoomAt(timestamp) : latest.Bounds;
                        drawing.DrawImage(latest, surface.Bounds, crop);
                        if (cameraBitmap is not null)
                        {
                            var w = Math.Max(2, width * 0.22);
                            var h = w * cameraBitmap.Size.Height / cameraBitmap.Size.Width;
                            h = Math.Min(h, height * 0.3);
                            var margin = Math.Min(width, height) * 0.026;
                            drawing.DrawImage(cameraBitmap, new Rect(width - w - margin, height - h - margin, w, h));
                        }
                    }
                    var media = MediaStreamSample.CreateFromDirect3D11Surface(surface, timestamp);
                    media.Duration = TimeSpan.FromTicks(videoIndex * TimeSpan.TicksPerSecond / Fps) - timestamp;
                    outstanding.TryAdd(surface, 0);
                    media.Processed += (_, _) => Release(surface);
                    request.Sample = media;
                }
                catch
                {
                    if (!Release(surface)) surface.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                request.Sample = null;
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref sampleError, ex, null);
                sender.NotifyError(MediaStreamSourceErrorStatus.Other);
            }
            finally { deferral.Complete(); }
        };

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var destination = await file.OpenAsync(FileAccessMode.ReadWrite);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)width;
            profile.Video.Height = (uint)height;
            profile.Video.Bitrate = (uint)Math.Clamp((long)width * height * 4, 850_000, 28_000_000);
            profile.Video.FrameRate.Numerator = Fps;
            profile.Video.FrameRate.Denominator = 1;
            if (audio is null) profile.Audio = null;
            var encoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
            var prepared = await encoder.PrepareMediaStreamSourceTranscodeAsync(stream, destination, profile);
            if (!prepared.CanTranscode)
                throw new InvalidOperationException($"Windows could not start MP4 encoding: {prepared.FailureReason}.");
            await prepared.TranscodeAsync();
            if (sampleError is not null) throw new InvalidOperationException("A recording frame could not be encoded.", sampleError);
            await destination.FlushAsync();
            if (videoIndex == 0) throw new InvalidOperationException("No video frames were recorded.");
        }
        catch (Exception) when (sampleError is not null)
        {
            throw new InvalidOperationException(sampleError.Message, sampleError);
        }
        finally
        {
            cameraBitmap?.Dispose();
            foreach (var surface in outstanding.Keys) Release(surface);
        }

        bool Release(CanvasRenderTarget surface)
        {
            if (!outstanding.TryRemove(surface, out _)) return false;
            surface.Dispose();
            return true;
        }

        void UpdateDesktop(Direct3D11CaptureFrame frame)
        {
            var size = frame.ContentSize;
            if (area.X + area.Width > size.Width || area.Y + area.Height > size.Height)
                throw new InvalidOperationException("The selected recording area changed size.");
            using var source = CanvasBitmap.CreateFromDirect3D11Surface(feed.Device, frame.Surface);
            using var drawing = latest.CreateDrawingSession();
            drawing.Clear(Microsoft.UI.Colors.Black);
            drawing.DrawImage(source, latest.Bounds, new Rect(area.X, area.Y, area.Width, area.Height));
        }

        async Task WaitAsync(TimeSpan timestamp)
        {
            var delay = timestamp - clock.Elapsed;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, stop);
            stop.ThrowIfCancellationRequested();
        }

        Rect ZoomAt(TimeSpan timestamp)
        {
            var down = NativeWindow.LeftPointerPressed();
            if (down && !pressed && NativeWindow.GetCursorPos(out var point))
            {
                var x = point.X - bounds.Left - area.X;
                var y = point.Y - bounds.Top - area.Y;
                if (x >= 0 && x < area.Width && y >= 0 && y < area.Height)
                {
                    focus = new Point(x * (double)width / area.Width, y * (double)height / area.Height);
                    lastClick = timestamp;
                }
            }
            pressed = down;
            if (lastClick == TimeSpan.MinValue) return latest.Bounds;
            var t = (timestamp - lastClick).TotalSeconds;
            var ramp = Math.Clamp(Math.Min(t / 0.23, (1.45 - t) / 0.42), 0, 1);
            var zoom = 1 + 0.55 * ramp * ramp * (3 - 2 * ramp);
            var w = width / zoom;
            var h = height / zoom;
            return new Rect(Math.Clamp(focus.X - w / 2, 0, width - w), Math.Clamp(focus.Y - h / 2, 0, height - h), w, h);
        }
    });
}
