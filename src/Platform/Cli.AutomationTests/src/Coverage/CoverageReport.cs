using System.Text;

namespace _42.Platform.Cli.AutomationTests.Coverage;

// The result of CoverageModel.Evaluate, with the messages of CommandCoverageTests.
public sealed class CoverageReport
{
    public CoverageReport(
        CoverageModel model,
        IReadOnlyDictionary<string, IReadOnlyList<CoversUsage>> coveredBy,
        IReadOnlyList<CoverageItem> uncovered,
        IReadOnlyList<CoverageItem> gatedOnly,
        IReadOnlyList<CoverageItem> unlistedExceptions,
        IReadOnlyList<string> staleExceptions,
        IReadOnlyList<string> invalidCovers,
        int usages)
    {
        Model = model;
        CoveredBy = coveredBy;
        Uncovered = uncovered;
        GatedOnly = gatedOnly;
        UnlistedExceptions = unlistedExceptions;
        StaleExceptions = staleExceptions;
        InvalidCovers = invalidCovers;
        Usages = usages;
    }

    public CoverageModel Model { get; }

    // The [Covers] that cover each item (by CoverageItem.Key).
    public IReadOnlyDictionary<string, IReadOnlyList<CoversUsage>> CoveredBy { get; }

    // Items no [Covers] names.
    public IReadOnlyList<CoverageItem> Uncovered { get; }

    // Items only feature-gated or skipped tests cover.
    public IReadOnlyList<CoverageItem> GatedOnly { get; }

    // Of GatedOnly, those coverage-exceptions.json does not list.
    public IReadOnlyList<CoverageItem> UnlistedExceptions { get; }

    // Entries of coverage-exceptions.json that name nothing, lack a reason, name an unknown feature, are listed twice, are
    // covered by a test that runs everywhere, or name another feature than their tests require.
    public IReadOnlyList<string> StaleExceptions { get; }

    // [Covers] that name something sform does not have, or sit on a method that is no test.
    public IReadOnlyList<string> InvalidCovers { get; }

    public int Usages { get; }

    // The uncovered items grouped by command in the tree's order, one line per kind, as a phase-5 author adds them:
    //   storyteller kv set [KeyValueSetCommand] (aliases: create)
    //     alias     create
    //     argument  arg1 (Value)
    //     option    --stdin, -i, --import
    //     value     --protection=standard, --protection=hsm
    public static string Grouped(IEnumerable<CoverageItem> items)
    {
        var builder = new StringBuilder();
        foreach (var group in items.GroupBy(item => item.Command))
        {
            var command = group.Key;
            builder.Append(command.Path).Append(" [").Append(command.ClassName).Append(']');
            if (command.Aliases.Count > 0)
            {
                builder.Append(" (aliases: ").Append(string.Join(", ", command.Aliases)).Append(')');
            }

            builder.AppendLine();
            foreach (var kind in Enum.GetValues<CoverageKind>())
            {
                var tokens = group.Where(item => item.Kind == kind)
                    .Select(item => kind == CoverageKind.Argument && item.Detail is not null ? $"{item.Token} ({item.Detail})" : item.Token)
                    .ToList();
                if (tokens.Count > 0)
                {
                    builder.Append("  ").Append(kind.ToString().ToLowerInvariant().PadRight(10)).AppendLine(string.Join(", ", tokens));
                }
            }
        }

        return builder.ToString();
    }

    // Counts of the items and of the uncovered ones, per kind: "commands 98 of 99, aliases 41 of 41, …".
    public string Summary(IReadOnlyList<CoverageItem> subset)
    {
        return string.Join(", ", Enum.GetValues<CoverageKind>().Select(kind =>
            $"{Plural(kind)} {subset.Count(item => item.Kind == kind)} of {Model.Items.Count(item => item.Kind == kind)}"));
    }

    public string DescribeUncovered()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{Uncovered.Count} of {Model.Items.Count} items of sform's command tree are covered by no [Covers] of the tests ({Usages} [Covers] read): {Summary(Uncovered)}.");
        builder.AppendLine("Each line lists what a test still has to cover, as [Covers(\"<command path>\", \"<item>\", …)]: the command when its path ends with the command's name, an alias typed in the path, options by each form, values as --option=value (of an argument as argN=value), arguments as argN. The context -p counts with any case of its command. See docs/Platform/Cli.AutomationTests/automation-tests.md#command-coverage.");
        builder.AppendLine();
        builder.Append(Grouped(Uncovered));
        return builder.ToString();
    }

    public string DescribeExceptions()
    {
        var builder = new StringBuilder();
        if (UnlistedExceptions.Count > 0)
        {
            builder.AppendLine($"{UnlistedExceptions.Count} item(s) are covered only by tests that need a feature of the target or never run, and {CoverageExceptions.FileName} does not list them (add each with the feature and the reason, or cover it by a test that runs everywhere):");
            foreach (var item in UnlistedExceptions)
            {
                var users = CoveredBy[item.Key];
                builder.AppendLine($"  {item}: {string.Join(", ", users.Select(user => user.IsSkipped ? $"{user.TestName} ([Skip])" : $"{user.TestName} ([RequiresFeature({string.Join(", ", user.Features)})])"))}");
            }
        }

        if (StaleExceptions.Count > 0)
        {
            builder.AppendLine($"{StaleExceptions.Count} entr(y/ies) of {CoverageExceptions.FileName} are stale:");
            foreach (var stale in StaleExceptions)
            {
                builder.AppendLine($"  {stale}");
            }
        }

        return builder.ToString();
    }

    private static string Plural(CoverageKind kind)
    {
        return kind switch
        {
            CoverageKind.Command => "commands",
            CoverageKind.Alias => "aliases",
            CoverageKind.Option => "option forms",
            CoverageKind.Argument => "arguments",
            _ => "values",
        };
    }
}
