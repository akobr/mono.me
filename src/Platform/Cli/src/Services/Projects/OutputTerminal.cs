using System;
using System.Text;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using Spectre.Console;

namespace _42.Platform.Cli.Services.Projects;

// What the console of a run can show: whether its standard output goes to a console at all (not to a file or a pipe,
// even when standard input or error is still a terminal), the encoding of its output, and whether it takes escape codes
// (on Windows, the virtual terminal mode that Spectre.Console turns on where it can; never a redirected output).
internal sealed record OutputTerminal(Func<bool> HasConsole, Func<Encoding?> OutputEncoding, Func<bool> TakesEscapeCodes)
{
    public static OutputTerminal Of(IExtendedConsole console)
    {
        return new OutputTerminal(
            () => console is SformConsole { WritesToConsole: true },
            () => console.Console?.Out?.Encoding,
            () => console.Console is { IsOutputRedirected: false }
                && (!OperatingSystem.IsWindows() || AnsiConsole.Profile.Capabilities.Ansi));
    }
}
