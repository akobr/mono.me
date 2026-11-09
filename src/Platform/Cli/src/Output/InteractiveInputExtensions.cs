using System.Collections.Generic;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;

namespace _42.Platform.Cli.Output;

// No command waits for an answer that cannot come. Before a prompt (Select, Confirm, Input, Password), a command calls
// one of these: when the standard input is redirected (a pipe, a file, /dev/null, a job without a terminal), it writes
// what sform would have asked and how to give it on the command line to standard error, and ends the command with an
// InteractionNeededException, which BaseContextCommand and Program turn into its exit code.
public static class InteractiveInputExtensions
{
    // False when the standard input is redirected.
    public static bool IsInteractive(this IExtendedConsole console)
    {
        return console.Console?.IsInputRedirected != true;
    }

    // A confirmation that an option skips (-y|--yes), or a command that only works in a terminal:
    // WARNING_INTERACTION_NEEDED.
    public static void EnsureInteractive(this IExtendedConsole console, string question, params string[] hints)
    {
        Ensure(console, ExitCodes.WARNING_INTERACTION_NEEDED, question, hints);
    }

    // A value that an option or argument supplies (the project, the organization, the value of a secret):
    // ERROR_WRONG_INPUT, as when it is missing on the command line.
    public static void EnsureInteractiveValue(this IExtendedConsole console, string question, params string[] hints)
    {
        Ensure(console, ExitCodes.ERROR_WRONG_INPUT, question, hints);
    }

    private static void Ensure(IExtendedConsole console, int exitCode, string question, IReadOnlyList<string> hints)
    {
        if (console.IsInteractive())
        {
            return;
        }

        var reason = $"sform cannot ask for {question}: the input is redirected.";
        console.WriteErrorLine($"! {reason}");

        foreach (var hint in hints)
        {
            console.WriteErrorLine($"  {hint}");
        }

        throw new InteractionNeededException(exitCode, string.Join(" ", hints.Prepend(reason)));
    }
}
