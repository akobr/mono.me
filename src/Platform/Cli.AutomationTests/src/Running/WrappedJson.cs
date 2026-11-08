using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests.Running;

// sform's WriteJson (src/Platform/Cli/src/Output/ExtendedConsoleExtensions.cs) renders the JSON with Spectre.Console.Json
// into a console of its own over a StringWriter, whose width is fixed at 80 columns (COLUMNS does not reach it), and only
// then puts the " n │ " gutter in front of each rendered line. So a token that does not fit the rest of a line moves to
// the next line, a word of a string value too, and a token longer than 80 columns (an API key, a long annotation key or
// value) is folded into lines of 80, inside its string literal. As printed, such a block is no JSON document and a key is
// on two lines, where neither the JSON reader nor the redaction of a secret would find it.
//
// Join puts the folds back: a numbered block that is no JSON document as printed, but is one with its folds joined, is
// replaced by its joined lines (numbered again); everything else stays as it is (a block that is JSON already, the
// formatted payloads of WriteNumberedText, any other text). Within such a block a line that ends inside a string goes on
// with the next line:
//   - without a separator when the line is a fold (80 columns without white space: a piece of one long token) or ends
//     with white space (the renderer kept the space at its end);
//   - with one space otherwise: the renderer drops the white space at the start of a line, so a run of spaces where a
//     string value wrapped comes back as one space (the one thing that cannot be restored).
// A line that ends outside a string ends a JSON line, as printed. Once the CLI renders WriteJson at the width of the text
// (a CLI fix), no block needs joining and Join changes nothing.
public static partial class WrappedJson
{
    // The width of the console WriteJson renders into (Spectre's default without a terminal).
    public const int RenderWidth = 80;

    // The text with the folded JSON blocks of WriteJson joined (see the class comment); the text itself when none is.
    public static string Join(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText) || !plainText.Contains('\n', StringComparison.Ordinal))
        {
            return plainText ?? string.Empty;
        }

        var lines = plainText.Split('\n');
        var output = new List<string>(lines.Length);
        var changed = false;
        var index = 0;
        while (index < lines.Length)
        {
            var block = NumberedRun(lines, index);
            if (block.Count == 0)
            {
                output.Add(lines[index]);
                index++;
                continue;
            }

            if (Rejoin(block) is { } joined)
            {
                var width = joined.Count.ToString(CultureInfo.InvariantCulture).Length;
                output.AddRange(joined.Select((line, number) => $"{(number + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width)} │ {line}"));
                changed = true;
            }
            else
            {
                output.AddRange(lines[index..(index + block.Count)]);
            }

            index += block.Count;
        }

        return changed ? string.Join('\n', output) : plainText;
    }

    // Whether the text is a JSON document (comments and trailing commas allowed, as sform's readers do).
    public static bool IsJson(string text)
    {
        if (text.Length == 0 || (text[0] != '{' && text[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The gutter-stripped texts of the numbered lines 1, 2, … that start at index (none when line index is not " 1 │ …").
    private static List<string> NumberedRun(string[] lines, int index)
    {
        var texts = new List<string>();
        var expected = 1;
        for (var at = index; at < lines.Length; at++)
        {
            var match = GutterPattern().Match(lines[at].TrimEnd('\r'));
            if (!match.Success
                || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number != expected)
            {
                break;
            }

            texts.Add(match.Groups["text"].Value);
            expected++;
        }

        return texts;
    }

    // The logical lines of a folded JSON block; null when the block is JSON as printed or is no JSON when joined either.
    private static List<string>? Rejoin(List<string> texts)
    {
        var asPrinted = string.Join('\n', texts.Select(text => text.TrimEnd()));
        if (asPrinted.Length == 0 || (asPrinted[0] != '{' && asPrinted[0] != '[') || IsJson(asPrinted))
        {
            return null;
        }

        var logical = new List<string>();
        var current = new StringBuilder();
        var inString = false;
        var escaped = false;
        for (var index = 0; index < texts.Count; index++)
        {
            var text = texts[index];
            if (index > 0)
            {
                if (inString)
                {
                    var previous = texts[index - 1];
                    var keptOrFolded = IsFold(previous) || (previous.Length > 0 && char.IsWhiteSpace(previous[^1]));
                    if (!keptOrFolded)
                    {
                        current.Append(' ');
                    }
                }
                else
                {
                    logical.Add(current.ToString().TrimEnd());
                    current.Clear();
                }
            }

            current.Append(text);
            foreach (var character in text)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (inString && character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = !inString;
                }
            }
        }

        logical.Add(current.ToString().TrimEnd());
        return logical.Count < texts.Count && IsJson(string.Join('\n', logical)) ? logical : null;
    }

    // A piece of a token longer than the render width: a whole line of it, without white space.
    private static bool IsFold(string line)
    {
        return line.Length == RenderWidth && !line.Any(char.IsWhiteSpace);
    }

    // "  12 │ text": the number, a space, the bar (│, or | / ? when an encoding lost it), an optional space, the text.
    [GeneratedRegex(@"^\s*(?<n>\d+) [│|?�] ?(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GutterPattern();
}
