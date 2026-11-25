#if DEBUG
using Windows.Graphics;

namespace RealCaptureX;

internal static class CaptureMathChecks
{
    public static void Run()
    {
        const int width = 96;
        const int height = 120;
        const int offset = 29;
        var source = new byte[width * (height + offset) * 4];
        new Random(78324).NextBytes(source);
        var input = new CaptureFrame(width, height + offset, source);
        var head = input.Crop(new RectInt32(0, 0, width, height));
        var next = input.Crop(new RectInt32(0, offset, width, height));
        var pixel = input.Crop(new RectInt32(17, 8, 1, 1));
        if (!pixel.Bgra.AsSpan().SequenceEqual(source.AsSpan((8 * width + 17) * 4, 4)))
            throw new InvalidOperationException("Cropping did not preserve the source pixel.");
        var stitch = new ScrollStitcher(head);
        if (stitch.Append(head) || !stitch.Append(next))
            throw new InvalidOperationException("Scrolling alignment did not distinguish stationary and shifted frames.");
        var stitched = stitch.Complete();
        if (stitched.Height != height + offset || !stitched.Bgra.AsSpan().SequenceEqual(source))
            throw new InvalidOperationException("Scrolling alignment did not preserve the source image.");
        var rotated = input.Transpose();
        if (rotated.Width != input.Height || rotated.Height != input.Width ||
            !rotated.Transpose().Bgra.AsSpan().SequenceEqual(source))
            throw new InvalidOperationException("Transposition changed the source image.");
        var shortcut = CaptureShortcut.ParseAll(["Ctrl+1", "Alt+F9", "Ctrl+Shift+R", "Ctrl+Alt+V", "Alt+Shift+F11"]);
        if (shortcut[1] != new CaptureShortcut(1, 0x78) || CaptureShortcut.TryParse("Ctrl+F12", out _))
            throw new InvalidOperationException("Shortcut validation accepted an incorrect virtual key.");
    }
}
#endif
