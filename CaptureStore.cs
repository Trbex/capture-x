using System.Globalization;
using System.Text.Json;
using Windows.Storage.Streams;

namespace RealCaptureX;

internal sealed record CaptureEntry(string Path, DateTimeOffset Created, string Mode, int Width, int Height)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

internal sealed record CaptureSettings(int Corner = 0, int Timeout = 12, bool ShowPreview = true, bool AutoCopy = false,
    bool IncludePointer = false, string[]? Shortcuts = null, ProductTheme Theme = ProductTheme.System)
{
    internal string[] KeyBindings => Shortcuts ?? CaptureShortcut.Defaults();
}

internal sealed class CaptureStore
{
    internal static readonly string Root = GetRoot();
    private readonly string captures = Path.Combine(Root, "Captures");
    private readonly string preferences = Path.Combine(Root, "settings.json");
    private const string Stamp = "yyyyMMdd'T'HHmmssfff";

    private static string GetRoot()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("REAL_CAPTURE_X_INSPECT") == "1")
            return Path.Combine(Path.GetTempPath(), "REAL Capture X QA");
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REAL Capture X");
    }

    public IReadOnlyList<CaptureEntry> ReadAll()
    {
        if (!Directory.Exists(captures)) return [];
        return Directory.EnumerateFiles(captures, "*.png")
            .Select(Parse).OfType<CaptureEntry>().OrderByDescending(entry => entry.Created).ToArray();
    }

    public async Task<CaptureEntry> AddAsync(CaptureFrame frame, string mode)
    {
        Directory.CreateDirectory(captures);
        var date = DateTimeOffset.UtcNow;
        var stamp = date.ToString(Stamp, CultureInfo.InvariantCulture);
        var path = Path.Combine(captures, $"{stamp}_{mode}_{frame.Width}x{frame.Height}_{Guid.NewGuid():N}.png");
        await WritePngAsync(path, frame, replace: false);
        return new CaptureEntry(path, date, mode, frame.Width, frame.Height);
    }

    internal static async Task WritePngAsync(string path, CaptureFrame frame, bool replace)
    {
        var pending = Path.Combine(Path.GetDirectoryName(path)!, $".realcapture-{Guid.NewGuid():N}.tmp");
        try
        {
            using var png = await frame.EncodeAsync();
            await using (var destination = File.Create(pending))
            using (var read = png.AsStreamForRead())
                await read.CopyToAsync(destination);
            File.Move(pending, path, replace);
        }
        finally
        {
            File.Delete(pending);
        }
    }

    private static CaptureEntry? Parse(string path)
    {
        var parts = Path.GetFileNameWithoutExtension(path).Split('_');
        if (parts.Length != 4 ||
            !DateTimeOffset.TryParseExact(parts[0], Stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var created)) return null;
        var size = parts[2].Split('x');
        if (size.Length != 2 || !int.TryParse(size[0], out var width) || !int.TryParse(size[1], out var height) ||
            width <= 0 || height <= 0) return null;
        return new CaptureEntry(path, created, parts[1], width, height);
    }

    public CaptureSettings ReadSettings()
    {
        try
        {
            if (!File.Exists(preferences)) return new CaptureSettings();
            var value = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(preferences)) ?? new();
            try { CaptureShortcut.ParseAll(value.KeyBindings); }
            catch (InvalidOperationException) { value = value with { Shortcuts = null }; }
            return value with
            {
                Corner = Math.Clamp(value.Corner, 0, 3),
                Timeout = Math.Clamp(value.Timeout, 0, 600),
                Theme = Enum.IsDefined(value.Theme) ? value.Theme : ProductTheme.System
            };
        }
        catch (JsonException)
        {
            return new CaptureSettings();
        }
    }

    public void SaveSettings(CaptureSettings settings)
    {
        Directory.CreateDirectory(Root);
        var pending = preferences + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(settings));
            File.Move(pending, preferences, overwrite: true);
        }
        finally
        {
            File.Delete(pending);
        }
    }
}
