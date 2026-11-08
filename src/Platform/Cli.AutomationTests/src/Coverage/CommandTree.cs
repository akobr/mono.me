namespace _42.Platform.Cli.AutomationTests.Coverage;

// The command tree of sform as McMaster.Extensions.CommandLineUtils builds it from the attributes of 42.Platform.Cli.dll
// (read by CommandTreeReader without running any code of the CLI): the commands from the root PlatformCommand along the
// [Subcommand] attributes, their names and aliases ([Command]), options ([Option], [VersionOption], also inherited from
// base classes such as BaseContextCommand) and arguments ([Argument]).
public sealed class CommandTree
{
    // The base class whose -p|--projectKey and -v|--view every context command inherits; its -p counts as covered by any
    // case of the command (the catalogue's rule), --projectKey, -v and --view do not.
    public const string ContextCommandType = "_42.Platform.Cli.Commands.BaseContextCommand";

    public const string ContextProjectForm = "-p";

    public CommandTree(CliCommand root, string assemblyPath, IReadOnlyDictionary<string, IReadOnlyList<string>?> enums)
    {
        Root = root;
        AssemblyPath = assemblyPath;
        Enums = enums;
        Commands = [.. root.Walk()];
    }

    public CliCommand Root { get; }

    // Every command, depth first in the order of the [Subcommand] attributes.
    public IReadOnlyList<CliCommand> Commands { get; }

    public string AssemblyPath { get; }

    // The member names of the enums asked for when the tree was read (coverage-values.json's "enum"), null for a type the
    // CLI's assemblies do not have.
    public IReadOnlyDictionary<string, IReadOnlyList<string>?> Enums { get; }

    // A command by its path as typed (aliases allowed, "sform" or nothing for the root); null when there is none.
    public CliCommand? Find(string path)
    {
        var node = Root;
        foreach (var word in Words(path))
        {
            node = node.Child(word);
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    // The words of a command path without a leading "sform".
    public static IReadOnlyList<string> Words(string path)
    {
        var words = (path ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 && words[0] == CliCommand.RootName ? words[1..] : words;
    }
}
