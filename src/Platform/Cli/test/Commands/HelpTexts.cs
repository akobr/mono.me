using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Cli.UnitTests.Commands;

// Help texts as the tests compare them: by lines, or as one line of words.
internal static class HelpTexts
{
    // The text as one line: the words of every line, separated by single spaces.
    public static string Unwrap(string? text)
    {
        return string.Join(' ', Words(text));
    }

    public static string[] Words(string? text)
    {
        return (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    // Lines without their ends, and without the empty one after a last line end.
    public static List<string> Lines(string? text)
    {
        var lines = (text ?? string.Empty).ReplaceLineEndings("\n").Split('\n').ToList();

        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
