using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace RealCaptureX;

internal sealed record CaptureFrame(int Width, int Height, byte[] Bgra, bool HasAlpha = false)
{
    public CaptureFrame Crop(RectInt32 area)
    {
        if (area.X < 0 || area.Y < 0 || area.Width <= 0 || area.Height <= 0 ||
            area.Width > Width - area.X || area.Height > Height - area.Y)
            throw new ArgumentOutOfRangeException(nameof(area));

        var pixels = new byte[checked(area.Width * area.Height * 4)];
        var rowBytes = area.Width * 4;
        for (var y = 0; y < area.Height; y++)
            System.Buffer.BlockCopy(Bgra, ((area.Y + y) * Width + area.X) * 4, pixels, y * rowBytes, rowBytes);
        return new CaptureFrame(area.Width, area.Height, pixels, HasAlpha);
    }

    public CaptureFrame Transpose()
    {
        var source = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(Bgra.AsSpan());
        var pixels = new byte[Bgra.Length];
        var destination = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                destination[x * Height + y] = source[y * Width + x];
        return new CaptureFrame(Height, Width, pixels, HasAlpha);
    }

    public async Task<InMemoryRandomAccessStream> EncodeAsync()
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, HasAlpha ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Ignore,
                (uint)Width, (uint)Height, 96, 96, Bgra);
            await encoder.FlushAsync();
            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
