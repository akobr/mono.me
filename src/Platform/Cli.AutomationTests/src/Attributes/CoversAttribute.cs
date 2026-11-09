// In the root namespace like AreaAttribute, so a test writes [Covers("story set", "-l")] without a using.
namespace _42.Platform.Cli.AutomationTests;

// What a test exercises of the command tree (spec §4.2), as the Covers column of the catalogue lists it
// (docs/Platform/Cli.AutomationTests/test-cases.md). Repeat it for each command of the steps, with the tokens of that
// command; a token the row lists once that the steps use on several commands goes on each of them (the context cases put
// --projectKey, -v and --view on every command they run). On a class it applies to every test of the class.
//   - The command path as the steps type it after sform ("story kv set"; "sform" is the root). It covers the command when
//     it ends with the command's canonical name ("story config" covers config, "story configuration" only the alias),
//     every alias it types anywhere ("story …" covers the alias story of storyteller), and every group it names that only
//     prints its help (sform, schema, schema type, …). The context option -p of a context command counts as covered by any
//     [Covers] of that command (every case passes it, see the catalogue's rules); --projectKey, -v and --view do not.
//   - The tokens of that command whose effect the test checks: options in the form used ("-l", "--labels": two items),
//     each value of an option with a fixed set as "--option=value" ("--protection=public", coverage-values.json), and
//     positional arguments as "arg0", "arg1", ….
// CommandCoverageTests (Coverage/) reads every [Covers] of the assembly, resolves it against the command tree of
// 42.Platform.Cli.dll and fails with each command, alias, option form, fixed value and argument no test covers, and with
// each [Covers] that names something sform does not have; CatalogueTests compares the [Covers] of a test with the Covers
// column of its row (CatalogueCovers).
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CoversAttribute(string command, params string[] items) : Attribute
{
    // The command path, e.g. "account", "story kv set", "account machine create".
    public string Command { get; } = command;

    // Options, fixed values and arguments of that command: "-r", "--resolved", "--protection=public", "arg0".
    public IReadOnlyList<string> Items { get; } = items;
}
