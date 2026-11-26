namespace RealCaptureX;

/// <summary>
/// Debug-only QA switches. <see cref="Profile"/> isolates data (own folder, mutex, export folder) so tests never
/// touch the user's history; <see cref="Inspect"/> additionally relaxes window behaviour (shown in switchers, not
/// excluded from capture) so test tools can see floating windows. Release builds always run the product path.
/// </summary>
internal static class QaMode
{
#if DEBUG
    internal static bool Inspect { get; } = Environment.GetEnvironmentVariable("REAL_CAPTURE_X_INSPECT") == "1";
    internal static bool Profile { get; } = Inspect || Environment.GetEnvironmentVariable("REAL_CAPTURE_X_PROFILE") == "qa";
#else
    internal static bool Inspect => false;
    internal static bool Profile => false;
#endif
}

/// <summary>QA-only trace written into the isolated profile; compiled out of Release builds.</summary>
internal static class Diagnostics
{
    private static readonly object gate = new();

    [System.Diagnostics.Conditional("DEBUG")]
    internal static void Log(string message)
    {
#if DEBUG
        if (!QaMode.Profile) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(CaptureStore.Root);
                File.AppendAllText(Path.Combine(CaptureStore.Root, "diagnostics.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
#endif
    }
}
