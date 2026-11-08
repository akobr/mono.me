using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// What the configuration and template commands print, read back for the checks of the Configurations and Templates
// sections (the Templates tests use it too):
//   - In(projectKey, view, args): the arguments with -p and -v of another view (a generated one, which exists by its data,
//     keeps a template or a type schema of a case away from every other test) or another project;
//   - LinesOf: the lines of a payload (--content-only, a file), NumberedLines those of a --format without --content-only
//     (WriteNumberedText, the gutter removed), LastJson the last model WriteJson printed (config patch prints the JSON
//     Patch first, then the saved model);
//   - ConfigurationVersions, TemplateVersions: the headers of config versions and template versions, whose rows
//     SformResult.VersionRows reads (top to bottom, the newest first);
//   - HasDiffLine: a deleted ('-') or added ('+') line of a diff (WriteDiffResult: "<old> <new> │ - <line>");
//   - SavedVersion: N of "… has been saved (version N)." of config edit and template edit;
//   - GitHubEnvNames, GitHubEnvValueOf: the heredoc entries of a GITHUB_ENV file (NAME<<ghadelimiter_…, the value, the
//     delimiter).
internal static partial class ConfigOutput
{
    // The header of config versions <key> ("Versions of the configuration for '<key>'"), for SformResult.VersionRows.
    public const string ConfigurationVersions = "Versions of the configuration for";

    // The header of template versions <type>, for SformResult.VersionRows.
    public const string TemplateVersions = "Versions of the template for";

    public static string[] In(string projectKey, string view, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        ArgumentNullException.ThrowIfNull(arguments);
        return [.. arguments, "-p", projectKey, "-v", view];
    }

    // The lines of a text with LF or CRLF line ends, without the empty line after the last line end.
    public static IReadOnlyList<string> LinesOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        while (lines.Count > 0 && lines[0].Trim().Length == 0)
        {
            lines.RemoveAt(0);
        }

        return lines;
    }

    // The lines of the first block of numbered lines (" 1 │ db:"), the gutter removed; fails the run's assertion when
    // there is none.
    public static IReadOnlyList<string> NumberedLines(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var blocks = SformResult.NumberedBlocks(result.PlainStdOut);
        return blocks.Count > 0
            ? LinesOf(blocks[0])
            : throw result.Fail("printed no numbered text (the line-number gutter of a --format without --content-only)");
    }

    // The last JSON object of the numbered blocks WriteJson printed.
    public static JsonNode LastJson(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        JsonNode? last = null;
        foreach (var block in SformResult.NumberedBlocks(result.PlainStdOut))
        {
            try
            {
                if (JsonNode.Parse(block) is JsonObject parsed)
                {
                    last = parsed;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // a numbered block that is no JSON object (formatted text)
            }
        }

        return last ?? throw result.Fail("printed no JSON object in numbered lines");
    }

    // A line of a diff with the sign ('-' deleted, '+' added) whose text ends with the given text (a trailing comma
    // allowed), e.g. HasDiffLine(diff, '-', "\"a\": 2").
    public static bool HasDiffLine(SformResult result, char sign, string text)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var pattern = $@"^[ \d]*[│|?�] {Regex.Escape(sign.ToString())} .*{Regex.Escape(text)},?\s*$";
        return Regex.IsMatch(result.PlainStdOut, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant);
    }

    // N of "… has been saved (version N)."; fails the run's assertion without it.
    public static long SavedVersion(SformResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var match = SavedPattern().Match(result.PlainStdOut);
        return match.Success
            ? long.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture)
            : throw result.Fail("printed no \"has been saved (version N).\"");
    }

    // The names of the heredoc entries of a GITHUB_ENV file, in order.
    public static IReadOnlyList<string> GitHubEnvNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return GitHubEnvEntry().Matches(text.Replace("\r\n", "\n", StringComparison.Ordinal)).Select(match => match.Groups["name"].Value).ToList();
    }

    // The value of the entry NAME of a GITHUB_ENV file (one line), or null.
    public static string? GitHubEnvValueOf(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var lines = LinesOf(text);
        for (var index = 0; index + 1 < lines.Count; index++)
        {
            var match = GitHubEnvEntry().Match(lines[index]);
            if (match.Success && match.Groups["name"].Value == name)
            {
                return lines[index + 1];
            }
        }

        return null;
    }

    // A date of a versions table ("yyyy-MM-dd HH:mm"; the time may wrap to the next line of its cell).
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant)]
    public static partial Regex DatePattern();

    [GeneratedRegex(@"has been saved \(version (?<version>\d+)\)\.", RegexOptions.CultureInvariant)]
    private static partial Regex SavedPattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_.\-]+)<<ghadelimiter_[0-9a-f]+$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GitHubEnvEntry();
}
