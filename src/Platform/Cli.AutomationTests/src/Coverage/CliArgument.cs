namespace _42.Platform.Cli.AutomationTests.Coverage;

// A positional argument: Index is its position on the command line (arg0, arg1, …), after McMaster's sort by order.
public sealed record CliArgument(int Index, string Name, string DeclaringType, string MemberName)
{
    public string Token => $"arg{Index}";
}
