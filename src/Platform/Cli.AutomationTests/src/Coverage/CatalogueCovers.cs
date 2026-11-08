namespace _42.Platform.Cli.AutomationTests.Coverage;

// The [Covers] of a test (method and class, resolved by CoverageModel.Resolve) against the Covers column of its row
// (CatalogueTests.EveryAutomatedCase_MatchesItsRow). The catalogue lists a command only when the case claims it, and a
// token once for all the commands of a context case, so this is not an equality:
//   - each command or alias token of the row is covered by a [Covers] (a path that ends with the command's name, types the
//     alias anywhere, or names the help-only group);
//   - a [Covers] without items covers a command the row lists, by the last word of its path;
//   - each option, value and argument token of the row is an item of at least as many commands of the test as the row
//     lists it: a token the row lists twice (arg0 of set and of get) needs two commands that cover it, while a token the
//     row lists once may be covered on any number of commands (a context case lists --projectKey once and covers it on
//     every command it runs; KV-POL-07 lists --protection=byok once for kv set, protect and import). Two paths to one
//     command ("story config" and "story configuration") are one command;
//   - every item token of a [Covers] is listed by the row; forms compare exactly, a value as the catalogue writes it
//     (ResolvedCovers.ItemTokens: "-r=reader" is "--role=Reader");
//   - a [Covers] that names something sform does not have is a difference too.
public static class CatalogueCovers
{
    public static IReadOnlyList<string> Compare(CatalogueCase row, IReadOnlyList<ResolvedCovers> resolved)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(resolved);

        if (resolved.Count == 0)
        {
            return [$"[Covers] is missing; the row covers {string.Join(", ", row.Covers)}."];
        }

        var differences = new List<string>();
        foreach (var invalid in resolved.Where(item => !item.IsValid))
        {
            differences.Add($"{invalid.Usage.Written}: {string.Join(" ", invalid.Problems)}");
        }

        var rowCommands = row.Covers.Where(token => !IsItem(token)).ToHashSet(StringComparer.Ordinal);
        var testCommands = resolved.SelectMany(item => item.CommandTokens).ToHashSet(StringComparer.Ordinal);
        foreach (var command in rowCommands.Where(command => !testCommands.Contains(command)))
        {
            differences.Add($"Covers: the row lists `{command}`, which no [Covers] covers (a path that ends with it or types it as an alias).");
        }

        foreach (var claim in resolved.Where(item => item.ItemTokens.Count == 0 && item.LeafWord is not null && !rowCommands.Contains(item.LeafWord)))
        {
            differences.Add($"Covers: {claim.Usage.Written} covers `{claim.LeafWord}`, which the row does not list.");
        }

        var rowItems = row.Covers.Where(IsItem)
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var testItems = resolved
            .GroupBy(item => item.Command?.Path ?? item.Usage.Covers.Command, StringComparer.Ordinal)
            .SelectMany(group => group.SelectMany(item => item.ItemTokens).Distinct(StringComparer.Ordinal))
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var token in rowItems.Keys.Union(testItems.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var inRow = rowItems.GetValueOrDefault(token);
            var inTest = testItems.GetValueOrDefault(token);
            if (inRow == 0)
            {
                differences.Add($"Covers: [Covers] list `{token}`{Times(inTest)}, the row does not.");
            }
            else if (inTest == 0)
            {
                differences.Add($"Covers: the row lists `{token}`{Times(inRow)}, no [Covers] does.");
            }
            else if (inTest < inRow)
            {
                differences.Add($"Covers: the row lists `{token}` {inRow} times (once per command that uses it), the [Covers] of {inTest} command(s) list it.");
            }
        }

        return differences;
    }

    // An option, a value or an argument token ("-r", "--protection=public", "arg0"); a command or an alias otherwise.
    public static bool IsItem(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.StartsWith('-') || (token.StartsWith("arg", StringComparison.Ordinal) && token.Length > 3 && token[3..].All(char.IsDigit));
    }

    private static string Times(int count) => count > 1 ? $" {count} times" : string.Empty;
}
