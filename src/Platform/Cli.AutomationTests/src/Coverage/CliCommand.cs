namespace _42.Platform.Cli.AutomationTests.Coverage;

public sealed class CliCommand
{
    public const string RootName = "sform";

    private readonly List<CliCommand> children = [];

    public CliCommand(string name, IReadOnlyList<string> aliases, string typeName, CliCommand? parent)
    {
        Name = name;
        Aliases = aliases;
        TypeName = typeName;
        Parent = parent;
        parent?.children.Add(this);
    }

    public string Name { get; }

    public IReadOnlyList<string> Aliases { get; }

    // The full name of the command's class, e.g. _42.Platform.Cli.Commands.KeyValues.KeyValueSetCommand.
    public string TypeName { get; }

    public string ClassName => TypeName[(TypeName.LastIndexOf('.') + 1)..];

    public CliCommand? Parent { get; }

    public IReadOnlyList<CliCommand> Children => children;

    public IReadOnlyList<CliOption> Options { get; set; } = [];

    public IReadOnlyList<CliArgument> Arguments { get; set; } = [];

    // A group that only prints its help (its OnExecute calls CommandLineApplication.ShowHelp): the root and the schema
    // groups. Naming it in a longer path runs it as far as McMaster is concerned, so a path through it covers it.
    public bool IsHelpOnly { get; set; }

    public bool IsRoot => Parent is null;

    // The canonical path below the root ("storyteller kv set"); "sform" for the root.
    public string Path => IsRoot ? RootName : string.Join(' ', Lineage().Select(command => command.Name));

    public IEnumerable<string> Names => [Name, .. Aliases];

    // The context option -p (of BaseContextCommand) when the command has it.
    public CliOption? ContextProjectOption => Options.FirstOrDefault(option => option.IsContextProject);

    // The option that has the form ("-p", "--projectKey"), exactly as McMaster matches it.
    public CliOption? FindOption(string form)
    {
        return Options.FirstOrDefault(option => option.Forms.Contains(form, StringComparer.Ordinal));
    }

    public CliCommand? Child(string word)
    {
        return children.FirstOrDefault(child => child.Name == word || child.Aliases.Contains(word, StringComparer.Ordinal));
    }

    public IEnumerable<CliCommand> Walk()
    {
        yield return this;
        foreach (var descendant in children.SelectMany(child => child.Walk()))
        {
            yield return descendant;
        }
    }

    public override string ToString()
    {
        return Path;
    }

    private IEnumerable<CliCommand> Lineage()
    {
        var chain = new List<CliCommand>();
        for (var node = this; node is not null && !node.IsRoot; node = node.Parent)
        {
            chain.Add(node);
        }

        chain.Reverse();
        return chain;
    }
}
