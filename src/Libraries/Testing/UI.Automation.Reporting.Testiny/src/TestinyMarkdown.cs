using System.Globalization;
using System.Text;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// Testiny reads a rich text field (a run's description, a comment) as markdown when its first line is the header
// "<!--markdown-->" (the spelling of the OpenAPI document and of the API quickstart's text; the quickstart's example
// writes "<!-- MARKDOWN -->").
public static class TestinyMarkdown
{
    public const string Header = "<!--markdown-->";

    private const string Special = "\\`*_[]<>|#";

    // The header line, then the body.
    public static string Document(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return $"{Header}\n{body}";
    }

    // Plain text on one line, with the markdown characters escaped.
    public static string Text(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 8);
        foreach (var character in OneLine(text))
        {
            if (Special.Contains(character, StringComparison.Ordinal))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    // An inline code span; a backtick in the text becomes an apostrophe.
    public static string Code(string? text)
    {
        return $"`{OneLine(text ?? string.Empty).Replace('`', '\'')}`";
    }

    // A fenced block longer than any run of backticks in the content, so the content cannot close it.
    public static string Fence(string content, string info = "text")
    {
        ArgumentNullException.ThrowIfNull(content);

        var longest = 0;
        var current = 0;
        foreach (var character in content)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}{info}\n{content.ReplaceLineEndings("\n").TrimEnd('\n')}\n{fence}";
    }

    // 812 B, 33.4 KB, 25.0 MB.
    public static string Size(long bytes)
    {
        return bytes switch
        {
            < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
            < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.0} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.0} MB"),
        };
    }

    // 0.84 s, 12.31 s, 2 min 5.0 s.
    public static string Duration(TimeSpan duration)
    {
        return duration.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds + (duration.Milliseconds / 1000d):0.0} s")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.00} s");
    }

    // 2026-10-31 18:40:01Z.
    public static string Time(DateTimeOffset time)
    {
        return time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    // At most length characters, ending with an ellipsis when cut.
    public static string Truncate(string text, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= length ? text : string.Concat(text.AsSpan(0, Math.Max(0, length - 3)), "...");
    }

    private static string OneLine(string text)
    {
        return text.ReplaceLineEndings(" ").Trim();
    }
}
