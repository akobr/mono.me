using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests.Running;

// The text of sform's output without its escape sequences. sform colours its JSON even with every stream redirected
// (WriteJson renders through a Spectre console with ANSI forced on), and a pseudo terminal adds cursor movements, erases
// and window titles. Plain keeps the characters a reader sees in order:
//   - SGR colours, modes, OSC titles and hyperlinks, charset selections and other escapes are removed;
//   - a cursor-forward (CSI n C) and an erase of characters (CSI n X), which a pseudo terminal's renderer writes for runs
//     of spaces, become n spaces;
//   - a cursor position (CSI r;c H or f), a cursor down or next line (CSI n B or E) become a line break, so text the
//     renderer placed on another row does not run into the row before;
//   - CR LF becomes LF, and a lone CR (a line redrawn in place) a line break as well.
// It is a stream view, not a screen emulator: a redrawn prompt shows every time it was drawn, which is what a test that
// expects a text needs.
public static partial class AnsiText
{
    public static string Plain(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var withoutEscapes = EscapePattern().Replace(text, Translate);
        var builder = new StringBuilder(withoutEscapes.Length);
        for (var index = 0; index < withoutEscapes.Length; index++)
        {
            var character = withoutEscapes[index];
            switch (character)
            {
                case '\r' when index + 1 < withoutEscapes.Length && withoutEscapes[index + 1] == '\n':
                    break;
                case '\r':
                    builder.Append('\n');
                    break;

                // BEL (the end of a title) and other control characters but tab and line feed are not text.
                case '\n' or '\t':
                    builder.Append(character);
                    break;
                case < ' ' or '\u007F':
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    // The lines of the plain text, without the line ends.
    public static IReadOnlyList<string> Lines(string? text)
    {
        var plain = Plain(text);
        if (plain.Length == 0)
        {
            return [];
        }

        var lines = plain.Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    // How much of raw text that is still being written can be read as plain text now: all of it, unless it ends inside an
    // escape sequence, which is cut off until its end arrives (so plain text of a growing output only grows at its end).
    public static int CompletePrefixLength(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var escape = raw.LastIndexOf('\u001b');
        if (escape < 0)
        {
            return raw.Length;
        }

        var tail = raw.AsSpan(escape);
        if (tail.Length < 2)
        {
            return escape;
        }

        switch (tail[1])
        {
            case '[':
                for (var index = 2; index < tail.Length; index++)
                {
                    if (tail[index] is >= '@' and <= '~')
                    {
                        return raw.Length;
                    }
                }

                return escape;
            case ']' or 'P' or 'X' or '^' or '_':
                return tail.IndexOf('\u0007') >= 0 || tail[2..].IndexOf("\u001b\\".AsSpan()) >= 0 ? raw.Length : escape;
            case '(' or ')' or '*' or '+':
                return tail.Length >= 3 ? raw.Length : escape;
            default:
                return raw.Length;
        }
    }

    private static string Translate(Match match)
    {
        var csi = match.Groups["csi"];
        if (!csi.Success)
        {
            return string.Empty;
        }

        var final = match.Groups["final"].Value;
        var parameters = match.Groups["params"].Value;
        return final switch
        {
            "C" or "X" => new string(' ', Math.Clamp(FirstNumber(parameters, 1), 1, 1000)),
            "H" or "f" or "B" or "E" => "\n",
            _ => string.Empty,
        };
    }

    private static int FirstNumber(string parameters, int fallback)
    {
        var first = parameters.Split(';')[0].TrimStart('?', '>', '=');
        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
    }

    // CSI (ESC [ or the 8-bit 0x9B) with its parameters, intermediates and final byte; OSC up to BEL or ST; DCS, SOS, PM
    // and APC strings up to ST; a charset designation (ESC ( B); any other two-character escape (ESC 7, ESC =, ESC M).
    [GeneratedRegex(
        @"(?<csi>(?:\x1B\[|\x9B)(?<params>[0-?]*)[ -/]*(?<final>[@-~]))"
        + @"|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)?"
        + @"|\x1B[PX^_][^\x1B]*(?:\x1B\\)?"
        + @"|\x1B[()*+][0-9A-Za-z]"
        + @"|\x1B[ -/]*[0-~]",
        RegexOptions.CultureInvariant)]
    private static partial Regex EscapePattern();
}
