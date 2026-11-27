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
        SpringChecks();
    }

    /// <summary>The closed-form spring the whole motion system relies on for velocity hand-off.</summary>
    private static void SpringChecks()
    {
        foreach (var spec in new[] { new SpringSpec(.6f, .3f), new SpringSpec(1f, .3f), new SpringSpec(1.4f, .3f), SpringSpec.Indicator })
        {
            var (x, v) = SpringMath.Step(-120, 900, 40, spec, 0);
            if (Math.Abs(x + 120) > 1e-9 || Math.Abs(v - 900) > 1e-6)
                throw new InvalidOperationException("Spring does not start at its initial position and velocity.");
            // Frame-rate independence: one 50 ms step equals ten 5 ms steps (e.g. 20 Hz vs 200 Hz).
            var (x1, v1) = SpringMath.Step(-120, 900, 40, spec, .05);
            double xs = -120, vs = 900;
            for (var i = 0; i < 10; i++) (xs, vs) = SpringMath.Step(xs, vs, 40, spec, .005);
            if (Math.Abs(x1 - xs) > 1e-6 || Math.Abs(v1 - vs) > 1e-4)
                throw new InvalidOperationException("Spring result depends on the frame rate.");
            // Reported velocity is the true derivative of the position.
            const double h = 1e-5;
            var (a, _) = SpringMath.Step(-120, 900, 40, spec, .08 - h);
            var (b, vm) = SpringMath.Step(-120, 900, 40, spec, .08);
            var (c, _) = SpringMath.Step(-120, 900, 40, spec, .08 + h);
            if (Math.Abs((c - a) / (2 * h) - vm) > .05)
                throw new InvalidOperationException("Spring velocity is not the derivative of its position.");
            var (rest, restVelocity) = SpringMath.Step(-120, 900, 40, spec, 5);
            if (Math.Abs(rest - 40) > .01 || Math.Abs(restVelocity) > .01)
                throw new InvalidOperationException("Spring does not settle on its target.");
        }
        // A launch just under the critical limit never overshoots.
        var arrive = new SpringSpec(1f, .36f);
        var launch = ProductMotion.Launch(new System.Numerics.Vector2(-324, 0), arrive).X;
        for (var t = 0.0; t < 2; t += .002)
            if (SpringMath.Step(-324, launch, 0, arrive, t).Position > .001)
                throw new InvalidOperationException("Launched arrival overshot its target.");
        if (Math.Abs(SpringMath.Project(1000) - 499) > .01)
            throw new InvalidOperationException("Momentum projection does not match Apple's deceleration model.");
        if (SpringMath.Rubberband(100, 300) >= 100 || SpringMath.Rubberband(200, 300) <= SpringMath.Rubberband(100, 300))
            throw new InvalidOperationException("Rubber-banding is not progressive.");
    }
}
#endif
