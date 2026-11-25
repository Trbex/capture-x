namespace RealCaptureX;

internal readonly record struct CaptureShortcut(uint Modifiers, uint Key)
{
    public static string[] Defaults() => ["Ctrl+Shift+1", "Ctrl+Shift+2", "Ctrl+Shift+3", "Ctrl+Shift+4", "Ctrl+Shift+5"];

    public static bool TryParse(string value, out CaptureShortcut shortcut)
    {
        shortcut = default;
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        uint flags = 0;
        foreach (var part in parts[..^1])
        {
            uint flag = part.ToUpperInvariant() switch { "ALT" => 1, "CTRL" => 2, "SHIFT" => 4, _ => 0 };
            if (flag == 0 || (flags & flag) != 0) return false;
            flags |= flag;
        }
        if ((flags & 3) == 0) return false;
        var key = parts[^1].ToUpperInvariant();
        if (key.Length == 1 && (char.IsAsciiLetterOrDigit(key[0])))
            shortcut = new CaptureShortcut(flags, key[0]);
        else if (key.StartsWith('F') && uint.TryParse(key.AsSpan(1), out var number) && number is >= 1 and <= 24 && number != 12)
            shortcut = new CaptureShortcut(flags, 0x6F + number);
        else
            return false;
        return true;
    }

    public static CaptureShortcut[] ParseAll(IReadOnlyList<string> values)
    {
        if (values.Count != 5) throw new InvalidOperationException("Five keyboard shortcuts are required.");
        var parsed = new CaptureShortcut[values.Count];
        for (var i = 0; i < values.Count; i++)
            if (string.IsNullOrWhiteSpace(values[i]) || !TryParse(values[i], out parsed[i]))
                throw new InvalidOperationException($"Use Ctrl or Alt and a key, for example Ctrl+Shift+{i + 1}.");
        if (parsed.Distinct().Count() != parsed.Length)
            throw new InvalidOperationException("Choose a different shortcut for each action.");
        return parsed;
    }
}
