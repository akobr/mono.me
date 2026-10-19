using System;
using System.Diagnostics;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Telemetry;

// What Program learns about one run, for the usage event: strings, flags and the caught exception only,
// never the CommandLineApplication, which is disposed with the host before the event is sent.
internal sealed class CommandRun
{
    // Started in Main.
    public Stopwatch Stopwatch { get; } = new();

    // The CommandPath of the selected command, or of the deepest matched one when parsing failed.
    public string? Command { get; set; }

    // CommandOutcome.HELP or VERSION when the command only showed the information.
    public string? Information { get; set; }

    // Ctrl+C was pressed.
    public bool Cancelled { get; set; }

    // The exception Main caught; only its type name is sent.
    public Exception? Exception { get; set; }

    // The exception reached the last catch of Main.
    public bool Crashed { get; set; }

    // From the root's OnParsingComplete, for every parse: sform assist parses twice and the last one wins.
    public void RecordParsing(CommandLineApplication selectedCommand)
    {
        Command = CommandPath.Of(selectedCommand);
        Information = selectedCommand.OptionHelp?.HasValue() == true
            ? CommandOutcome.HELP
            : selectedCommand.OptionVersion?.HasValue() == true
                ? CommandOutcome.VERSION
                : null;
    }
}
