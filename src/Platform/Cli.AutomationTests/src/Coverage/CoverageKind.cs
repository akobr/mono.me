namespace _42.Platform.Cli.AutomationTests.Coverage;

public enum CoverageKind
{
    // The command itself, by its canonical name.
    Command,

    // An alias of the command.
    Alias,

    // One form of an option ("-p" and "--projectKey" are two items).
    Option,

    // A positional argument, "arg0".
    Argument,

    // One value of an option with a fixed set, "--protection=public".
    Value,
}
