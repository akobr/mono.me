using System.Globalization;

namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// The text forms of the STORYTELLER_* values.
internal static class SettingValues
{
    private static readonly (string Suffix, double Milliseconds)[] DurationUnits =
    [
        ("ms", 1),
        ("s", 1_000),
        ("m", 60_000),
        ("h", 3_600_000),
    ];

    // 500ms, 30s, 5m, 1h, plain seconds (30) or TimeSpan text (00:00:30); never negative.
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var value = text.Trim();

        // Before TimeSpan text, which reads a plain 30 as thirty days.
        if (TryParseNumber(value, out var seconds))
        {
            return TryCreate(seconds * 1_000, out duration);
        }

        // ms first: 500ms ends with s too.
        foreach (var (suffix, milliseconds) in DurationUnits)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && TryParseNumber(value[..^suffix.Length].TrimEnd(), out var amount))
            {
                return TryCreate(amount * milliseconds, out duration);
            }
        }

        if (value.Contains(':', StringComparison.Ordinal)
            && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span)
            && span >= TimeSpan.Zero)
        {
            duration = span;
            return true;
        }

        return false;
    }

    // true, false (any case), 1 or 0.
    public static bool TryParseBoolean(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
                value = true;
                return true;

            case "false":
            case "0":
                value = false;
                return true;

            default:
                value = false;
                return false;
        }
    }

    // resolved or effective, any case; not the numbers Enum.TryParse would accept.
    public static bool TryParseMode(string text, out StorytellerReadMode mode)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "resolved":
                mode = StorytellerReadMode.Resolved;
                return true;

            case "effective":
                mode = StorytellerReadMode.Effective;
                return true;

            default:
                mode = StorytellerReadMode.Resolved;
                return false;
        }
    }

    private static bool TryParseNumber(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    private static bool TryCreate(double milliseconds, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        if (milliseconds < 0 || milliseconds >= TimeSpan.MaxValue.TotalMilliseconds)
        {
            return false;
        }

        duration = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}
