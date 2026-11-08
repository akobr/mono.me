// In the root namespace like AreaAttribute, so a test writes [Covers("story set", "-l")] without a using.
namespace _42.Platform.Cli.AutomationTests;

// What a test exercises of the command tree (spec §4.2), as the Covers column of the catalogue lists it
// (docs/Platform/Cli.AutomationTests/test-cases.md): the command path as the steps type it after sform, which covers
// that command and every alias the path uses ("story configuration" covers the alias configuration of config), and the
// tokens of that command whose effect the test checks: options in the form used ("-l", "--labels"), each value of an
// option with a fixed set as "--option=value" ("--protection=public"), and positional arguments as "arg0", "arg1", ….
// Repeat it for each command of the steps. "sform" is the root. The context options -p of the context commands are not
// listed (every case passes them, see the catalogue's rules). Phase 4 adds CommandCoverageTests, which reads every
// [Covers] of the assembly, resolves the aliases against the command tree of 42.Platform.Cli.dll and fails with each
// command, alias, option form, fixed value and argument no test covers.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CoversAttribute(string command, params string[] items) : Attribute
{
    // The command path, e.g. "account", "story kv set", "account machine create".
    public string Command { get; } = command;

    // Options, fixed values and arguments of that command: "-r", "--resolved", "--protection=public", "arg0".
    public IReadOnlyList<string> Items { get; } = items;
}
