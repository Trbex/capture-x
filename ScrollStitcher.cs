namespace RealCaptureX;

internal sealed class ScrollStitcher
{
    private readonly List<byte[]> parts;
    private readonly int width;
    private CaptureFrame previous;
    public int Height { get; private set; }
    public bool AtLimit => Height >= Math.Min(14000, 25_000_000 / width);

    public ScrollStitcher(CaptureFrame first)
    {
        previous = first;
        width = first.Width;
        Height = first.Height;
        parts = [first.Bgra];
    }

    public bool Append(CaptureFrame current)
    {
        if (current.Width != previous.Width || current.Height != previous.Height)
            throw new InvalidOperationException("The scrolling view changed size.");

        var stationary = Difference(previous, current, 0);
        if (stationary < 1.15 || AtLimit) return false;
        var best = double.MaxValue;
        var match = 0;
        for (var shift = 1; shift <= current.Height * 3 / 4; shift++)
        {
            var difference = Difference(previous, current, shift);
            if (difference < best)
            {
                best = difference;
                match = shift;
            }
        }
        if (match == 0 || best >= Math.Min(6, stationary * 0.6)) return false;

        var maxRows = Math.Min(14000, 25_000_000 / current.Width);
        var rows = Math.Min(match, maxRows - Height);
        var rowBytes = current.Width * 4;
        var tail = new byte[rows * rowBytes];
        Buffer.BlockCopy(current.Bgra, (current.Height - match) * rowBytes, tail, 0, tail.Length);
        parts.Add(tail);
        Height += rows;
        previous = current;
        return true;
    }

    internal static double Difference(CaptureFrame previous, CaptureFrame current, int offset)
    {
        var width = previous.Width;
        var overlap = previous.Height - offset;
        var xStep = Math.Max(1, width / 30);
        var margin = width / 12;
        var startY = overlap / 10;
        var stepY = Math.Max(1, (overlap - startY * 2) / 26);
        long score = 0;
        var count = 0;
        for (var y = startY; y < overlap - startY; y += stepY)
        {
            var baseA = (y + offset) * width * 4;
            var baseB = y * width * 4;
            for (var x = margin; x < width - margin; x += xStep)
            {
                var a = baseA + x * 4;
                var b = baseB + x * 4;
                score += Math.Abs(previous.Bgra[a] - current.Bgra[b]);
                score += Math.Abs(previous.Bgra[a + 1] - current.Bgra[b + 1]);
                score += Math.Abs(previous.Bgra[a + 2] - current.Bgra[b + 2]);
                count += 3;
            }
        }
        return (double)score / Math.Max(1, count);
    }

    public CaptureFrame Complete()
    {
        var result = new byte[checked(width * Height * 4)];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return new CaptureFrame(width, Height, result);
    }
}
