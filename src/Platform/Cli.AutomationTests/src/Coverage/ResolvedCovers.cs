namespace _42.Platform.Cli.AutomationTests.Coverage;

// A [Covers] resolved against the command tree: the command its path leads to, the items it covers, and what it names
// that sform does not have. CommandTokens and ItemTokens are its tokens as the catalogue's Covers column writes them
// (CatalogueTests compares them with the row).
public sealed class ResolvedCovers
{
    public ResolvedCovers(CoversUsage usage, CliCommand? command, IReadOnlyList<CoverageItem> items, IReadOnlyList<string> commandTokens, IReadOnlyList<string> itemTokens, IReadOnlyList<string> problems)
    {
        Usage = usage;
        Command = command;
        Items = items;
        CommandTokens = commandTokens;
        ItemTokens = itemTokens;
        Problems = problems;
    }

    public CoversUsage Usage { get; }

    // The command of the path (its last word); null when the path names no command.
    public CliCommand? Command { get; }

    // What it covers: the command when the path ends with its canonical name, each alias the path types, each help-only
    // group the path names, the command's context -p, and its items.
    public IReadOnlyList<CoverageItem> Items { get; }

    // The words of the path that cover a command or an alias (the leaf by its canonical name, an alias anywhere, a
    // help-only group, sform).
    public IReadOnlyList<string> CommandTokens { get; }

    // The items as the catalogue writes them: as written ("-r", "arg0"), but a value by the option's long form and the
    // value of coverage-values.json ("-r=reader" is "--role=Reader").
    public IReadOnlyList<string> ItemTokens { get; }

    // The leaf word of the path as typed; null for an empty path.
    public string? LeafWord => CommandTree.Words(Usage.Covers.Command) is { Count: > 0 } words ? words[^1] : Usage.Covers.Command.Trim() == CliCommand.RootName ? CliCommand.RootName : null;

    public IReadOnlyList<string> Problems { get; }

    public bool IsValid => Problems.Count == 0;
}
