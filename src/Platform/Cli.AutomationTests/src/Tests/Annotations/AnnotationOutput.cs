using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// What the annotation commands print, read from the plain standard output of a run:
//   - the lists of sform story (StorytellerListCommand): a header "# Annotations" ("# Responsibilities", "# Usages", …,
//     one per query), then a line "- <key>" per annotation, with " [disabled]", " [scheduled]", " [expired]" or
//     " [deleted]" after the key of an annotation that is not active, and at the end of a page "More … available, use
//     continuation token to retrieve next page:" followed by the token on its own line;
//   - the tables of story deleted and story history (a row per annotation or entry, the key or the time first);
//   - the trees of story -t (a line "<name> [subject]", "<name> [context]", "<name> [responsibility]" per node).
// sform renders the lines through the toolkit's documents with a margin of two spaces, so every reader trims them.
internal static partial class AnnotationOutput
{
    public const string ContinuationLabel = "use continuation token to retrieve next page:";

    // The keys of the "- <key>" lines of the whole output, in their order.
    public static IReadOnlyList<string> Keys(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Keys(result.Lines);
    }

    // The keys of the "- <key>" lines below the header "# <header>" up to the next header (or the end).
    public static IReadOnlyList<string> Section(SformResult result, string header)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(header);

        var lines = result.Lines.Select(line => line.Trim()).ToList();
        var start = lines.IndexOf($"# {header}");
        if (start < 0)
        {
            throw result.Fail($"printed no section '# {header}'");
        }

        var end = lines.FindIndex(start + 1, line => line.StartsWith("# ", StringComparison.Ordinal));
        return Keys(lines.Skip(start + 1).Take((end < 0 ? lines.Count : end) - start - 1));
    }

    // The continuation token a list printed at the end of its page, or null when it printed none (the last page). A token
    // longer than the width sform renders at continues on the next lines, which are joined.
    public static string? ContinuationToken(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var lines = result.Lines;
        var label = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Contains(ContinuationLabel, StringComparison.Ordinal))
            {
                label = index;
            }
        }

        if (label < 0)
        {
            return null;
        }

        var token = string.Concat(lines.Skip(label + 1).TakeWhile(line => line.Trim().Length > 0).Select(line => line.Trim()));
        return token.Length > 0 ? token : throw result.Fail("printed the label of a continuation token without the token");
    }

    // The keys that start the rows of a table (story deleted), in their order.
    public static IReadOnlyList<string> TableKeys(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Lines
            .Select(line => line.Trim().Split(' ', 2)[0])
            .Where(token => KeyPattern().IsMatch(token))
            .ToList();
    }

    // The row of a table (or any line) whose first word is the key; null when there is none.
    public static string? RowOf(SformResult result, string key)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return result.Lines.Select(line => line.Trim()).FirstOrDefault(line => line.Split(' ', 2)[0] == key);
    }

    // The rows of the history table: the lines that start with a time ("yyyy-MM-dd HH:mm").
    public static IReadOnlyList<string> HistoryRows(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Lines.Select(line => line.Trim()).Where(line => HistoryRowPattern().IsMatch(line)).ToList();
    }

    // The index of the first line at or after start that contains the text; -1 when none does.
    public static int IndexOfLine(IReadOnlyList<string> lines, string text, int start = 0)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(text);
        for (var index = Math.Max(start, 0); index < lines.Count; index++)
        {
            if (lines[index].Contains(text, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    // The trimmed lines of the output that hold the text.
    public static IReadOnlyList<string> LinesWith(SformResult result, string text)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(text);
        return result.Lines.Select(line => line.Trim()).Where(line => line.Contains(text, StringComparison.Ordinal)).ToList();
    }

    private static List<string> Keys(IEnumerable<string> lines)
    {
        return lines
            .Select(line => ListLinePattern().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToList();
    }

    [GeneratedRegex(@"^\s*-\s+(?<key>(?:rst|sbt|unt|usg|cnt|exe|uxe)\.\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ListLinePattern();

    [GeneratedRegex(@"^(?:rst|sbt|unt|usg|cnt|exe|uxe)\.\S+$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}\s", RegexOptions.CultureInvariant)]
    private static partial Regex HistoryRowPattern();
}
