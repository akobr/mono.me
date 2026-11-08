using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.ConfigurationFormats;

// JSON paths for messages and test vectors, the same in every language: $ for the root, .name for a plain property
// name, ['name'] for any other, [0] for an array item. Parse reads the same syntax back, so a printed path can be given
// as input (the name of an inline property of sform, for example).
public static class JsonPaths
{
    public const string Root = "$";

    private const string PlainNameRule = "a name of ASCII letters, digits, '_' and '-' that starts with an ASCII letter or '_'";

    public static string Property(string parent, string name)
    {
        return IsPlain(name)
            ? $"{parent}.{name}"
            : $"{parent}['{Escape(name)}']";
    }

    public static string Item(string parent, int index)
    {
        return $"{parent}[{index}]";
    }

    // $ followed by every step, as Property and Item print it. A segment with a name is a property whatever its index;
    // Parse(Of(steps)) gives back steps made with DocumentSegment.Property and Item.
    public static string Of(IEnumerable<DocumentSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var path = Root;

        foreach (var segment in segments)
        {
            if (segment.IsIndex)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(segment.Index, nameof(segments));
                path = Item(path, segment.Index);
            }
            else
            {
                path = Property(path, segment.Name!);
            }
        }

        return path;
    }

    // A whole path: $ and steps that each start with . or [, or a first step without a dot and then such steps. $ alone
    // is the root, with no steps.
    public static IReadOnlyList<DocumentSegment> Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var segments = ParsePrefix(path, out var length);

        if (length < path.Length)
        {
            throw new JsonPathFormatException(
                $"After a step, '.', '[' or the end of the path is expected; write a name with other characters than {PlainNameRule} as ['name'].",
                length);
        }

        return segments;
    }

    // A path at the start of text, as Parse reads it. It stops before the first character that cannot continue the path,
    // '=' for example, and length is the number of characters read; a text where no path starts throws at 0.
    // Inside ['…'], \' and \\ escape the quote and the backslash, and \n, \r, \t and \uXXXX (hex digits in any case)
    // read what the printer writes for a control character or a #. Throws JsonPathFormatException for a step that is not
    // complete or not valid.
    public static IReadOnlyList<DocumentSegment> ParsePrefix(string text, out int length)
    {
        ArgumentNullException.ThrowIfNull(text);

        var segments = new List<DocumentSegment>();
        var position = 0;

        if (position < text.Length && text[position] == '$')
        {
            position++;

            // $schema is not the root and a step without a dot: after $, every step keeps its . or [ as printed.
            if (position < text.Length && (IsPlainPart(text[position]) || text[position] == '$'))
            {
                throw new JsonPathFormatException(
                    "After $, '.', '[' or the end of the path is expected; a name that starts with $ is written ['$name'].",
                    position);
            }
        }
        else if (position < text.Length && IsPlainStart(text[position]))
        {
            segments.Add(DocumentSegment.Property(ReadPlainName(text, ref position)));
        }
        else if (position < text.Length && text[position] == '[')
        {
            segments.Add(ReadBracketedStep(text, ref position));
        }
        else
        {
            throw new JsonPathFormatException(
                position < text.Length
                    ? $"A path starts with $, with {PlainNameRule}, or with ['name'] for any other name."
                    : "A path is empty; it starts with $, with a name, or with ['name'].",
                position);
        }

        while (position < text.Length)
        {
            if (text[position] == '.')
            {
                position++;

                if (position < text.Length && IsPlainStart(text[position]))
                {
                    segments.Add(DocumentSegment.Property(ReadPlainName(text, ref position)));
                    continue;
                }

                throw new JsonPathFormatException(
                    position < text.Length && text[position] == '['
                        ? "A step in brackets follows without a dot: write a['name'] or a[0], not a.['name'] or a.[0]."
                        : $"After '.', {PlainNameRule} is expected; write any other name as ['name'].",
                    position);
            }

            if (text[position] == '[')
            {
                segments.Add(ReadBracketedStep(text, ref position));
                continue;
            }

            break;
        }

        length = position;
        return segments;
    }

    // ASCII letters, digits, _ and -, starting with an ASCII letter or _.
    private static bool IsPlain(string name)
    {
        if (name.Length == 0 || !IsPlainStart(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!IsPlainPart(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPlainStart(char character)
    {
        return char.IsAsciiLetter(character) || character == '_';
    }

    private static bool IsPlainPart(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '_' or '-';
    }

    // At the first character of a plain name, which IsPlainStart accepts.
    private static string ReadPlainName(string text, ref int position)
    {
        var start = position;

        while (position < text.Length && IsPlainPart(text[position]))
        {
            position++;
        }

        return text[start..position];
    }

    // At [: an index, [0], or a quoted name, ['name'].
    private static DocumentSegment ReadBracketedStep(string text, ref int position)
    {
        position++;

        var step = position < text.Length && text[position] == '\''
            ? DocumentSegment.Property(ReadQuotedName(text, ref position))
            : position < text.Length && char.IsAsciiDigit(text[position])
                ? DocumentSegment.Item(ReadIndex(text, ref position))
                : throw new JsonPathFormatException(
                    "After '[', an index (digits) or a quoted name ('name') is expected.",
                    position);

        if (position >= text.Length || text[position] != ']')
        {
            throw new JsonPathFormatException(
                step.IsIndex
                    ? "An index is digits closed by ']'."
                    : "A quoted name is closed by ']' right after its closing quote.",
                position);
        }

        position++;
        return step;
    }

    // At the opening quote; ends after the closing one.
    private static string ReadQuotedName(string text, ref int position)
    {
        var builder = new StringBuilder();
        position++;

        while (true)
        {
            if (position >= text.Length)
            {
                throw new JsonPathFormatException("A quoted name is not closed; a ' is expected, and a ' inside the name is written \\'.", position);
            }

            var character = text[position];

            if (character == '\'')
            {
                position++;
                return builder.ToString();
            }

            if (character != '\\')
            {
                builder.Append(character);
                position++;
                continue;
            }

            position++;

            switch (position < text.Length ? text[position] : '\0')
            {
                case '\\' or '\'':
                    builder.Append(text[position]);
                    position++;
                    break;

                case 'n':
                    builder.Append('\n');
                    position++;
                    break;

                case 'r':
                    builder.Append('\r');
                    position++;
                    break;

                case 't':
                    builder.Append('\t');
                    position++;
                    break;

                case 'u':
                    position++;
                    builder.Append(ReadHexCodeUnit(text, ref position));
                    break;

                default:
                    throw new JsonPathFormatException(
                        "A backslash in a quoted name starts one of the escapes \\', \\\\, \\n, \\r, \\t or \\uXXXX.",
                        position);
            }
        }
    }

    // The four hex digits of \uXXXX, in any case.
    private static char ReadHexCodeUnit(string text, ref int position)
    {
        var value = 0;

        for (var count = 0; count < 4; count++)
        {
            var digit = position < text.Length ? HexValue(text[position]) : -1;

            if (digit < 0)
            {
                throw new JsonPathFormatException("\\u in a quoted name is followed by four hex digits.", position);
            }

            value = (value * 16) + digit;
            position++;
        }

        return (char)value;
    }

    private static int HexValue(char character)
    {
        return character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'A' and <= 'F' => character - 'A' + 10,
            >= 'a' and <= 'f' => character - 'a' + 10,
            _ => -1,
        };
    }

    // At the first digit: 0, or digits without a leading zero, at most int.MaxValue.
    private static int ReadIndex(string text, ref int position)
    {
        var start = position;
        long value = 0;

        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            if (position > start && text[start] == '0')
            {
                throw new JsonPathFormatException("An index has no leading zero.", start);
            }

            value = (value * 10) + (text[position] - '0');

            if (value > int.MaxValue)
            {
                throw new JsonPathFormatException(
                    $"An index is at most {int.MaxValue.ToString(CultureInfo.InvariantCulture)}.",
                    start);
            }

            position++;
        }

        return (int)value;
    }

    // \ and ' with a backslash, C0 control characters and DEL as \n, \r, \t or \uXXXX, and # as \u0023. A crafted name
    // then cannot start a GitHub workflow command in a message, a log line or stderr: neither a new line for ::command::
    // nor the ##[command] of the runner's older syntax, which the runner finds anywhere in a line. C1 controls, U+2028
    // and U+2029 are kept, as the vectors pin for every language.
    private static string Escape(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            switch (character)
            {
                case '\\' or '\'':
                    builder.Append('\\').Append(character);
                    break;

                case '\n':
                    builder.Append("\\n");
                    break;

                case '\r':
                    builder.Append("\\r");
                    break;

                case '\t':
                    builder.Append("\\t");
                    break;

                case < ' ' or '\u007F' or '#':
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
