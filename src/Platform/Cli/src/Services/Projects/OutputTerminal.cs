using System;
using System.Text;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using Spectre.Console;

namespace _42.Platform.Cli.Services.Projects;

// What the console of a run can show: whether it is a console at all, the encoding of its output, and whether it takes
// escape codes (on Windows, the virtual terminal mode that Spectre.Console turns on where it can).
internal sealed record OutputTerminal(Func<bool> HasConsole, Func<Encoding?> OutputEncoding, Func<bool> TakesEscapeCodes)
{
    public static OutputTerminal Of(IExtendedConsole console)
    {
        return new OutputTerminal(
            () => console is SformConsole { HasConsole: true },
            () => console.Console?.Out?.Encoding,
            () => !OperatingSystem.IsWindows() || AnsiConsole.Profile.Capabilities.Ansi);
    }
}
