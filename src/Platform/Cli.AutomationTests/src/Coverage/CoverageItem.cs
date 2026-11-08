namespace _42.Platform.Cli.AutomationTests.Coverage;

// One thing of the command tree a test has to cover (spec §4.1): a command, an alias, an option form, an argument or a
// fixed value, of a command. Token is how a [Covers] names it: the command's name, the alias, the form, argN, or
// "<long form>=<value>" with the value as coverage-values.json writes it.
public sealed record CoverageItem(CliCommand Command, CoverageKind Kind, string Token, string? Detail = null)
{
    public string Key => $"{Command.Path}|{Kind}|{Token}";

    public override string ToString()
    {
        return $"{Command.Path}: {Kind.ToString().ToLowerInvariant()} {Token}{(Detail is null ? string.Empty : $" ({Detail})")}";
    }
}
