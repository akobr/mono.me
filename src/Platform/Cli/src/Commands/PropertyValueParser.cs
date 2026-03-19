using System;
using System.Globalization;

namespace _42.Platform.Cli.Commands;

/// <summary>
/// Provides type-aware parsing of raw command-line property string values.
/// Intended for use wherever key=value pairs are accepted as CLI options
/// (e.g. <c>--custom-properties</c>, <c>--properties</c>).
/// </summary>
public static class PropertyValueParser
{
    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss.fffK",
    };

    /// <summary>
    /// Parses <paramref name="value"/> to the most specific CLR type possible.
    /// The following types are tried in order:
    /// <list type="number">
    ///   <item><see cref="bool"/> — <c>true</c> / <c>false</c> (case-insensitive)</item>
    ///   <item><see cref="long"/> — whole numbers</item>
    ///   <item><see cref="double"/> — decimal / scientific-notation numbers (invariant culture)</item>
    ///   <item><see cref="DateTimeOffset"/> — ISO&nbsp;8601 date or date-time strings</item>
    ///   <item><see cref="TimeSpan"/> — duration strings such as <c>1:30:00</c></item>
    ///   <item><see cref="string"/> — original value returned as-is</item>
    /// </list>
    /// </summary>
    /// <param name="value">The raw string value supplied on the command line.</param>
    /// <returns>The parsed value boxed as <see cref="object"/>.</returns>
    public static object ParseValue(string value)
    {
        if (bool.TryParse(value, out var boolResult))
        {
            return boolResult;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longResult))
        {
            return longResult;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var doubleResult))
        {
            return doubleResult;
        }

        if (DateTimeOffset.TryParseExact(value, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateResult))
        {
            return dateResult;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeResult))
        {
            return timeResult;
        }

        return value;
    }
}
