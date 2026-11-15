using System;
using System.Diagnostics;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Telemetry;

// What Program learns about one run, for the usage event and the error reports: strings, flags and the caught exception
// only, never the CommandLineApplication, which is disposed with the host before the event is sent.
internal sealed class CommandRun
{
    private CommandLineValues _argumentValues = CommandLineValues.Empty;
    private CommandLineValues _modelValues = CommandLineValues.Empty;

    // Started in Main.
    public Stopwatch Stopwatch { get; } = new();

    // The CommandPath of the selected command, or of the deepest matched one when parsing failed.
    public string? Command { get; set; }

    // The command path and the names of the options given; null before parsing.
    public CommandUsage? Usage { get; private set; }

    // The values the reports must not show: of args and of the model.
    public CommandLineValues Values => _argumentValues.Merge(_modelValues);

    // CommandOutcome.HELP or VERSION when the command only showed the information.
    public string? Information { get; set; }

    // Ctrl+C was pressed.
    public bool Cancelled { get; set; }

    // The exception Main caught; only its type name is sent.
    public Exception? Exception { get; set; }

    // The exception reached the last catch of Main.
    public bool Crashed { get; set; }

    // From the root's OnParsingComplete, for every parse: sform assist parses twice and the last one wins. The options
    // hold only what was given there: the subcommands' handlers, which write the defaults of the model, run later.
    public void RecordParsing(CommandLineApplication selectedCommand)
    {
        RecordUsage(selectedCommand);
        Information = selectedCommand.OptionHelp?.HasValue() == true
            ? CommandOutcome.HELP
            : selectedCommand.OptionVersion?.HasValue() == true
                ? CommandOutcome.VERSION
                : null;
    }

    // A parse that failed: the deepest command matched, with the options parsed before the failure.
    public void RecordFailedParsing(CommandLineApplication command)
    {
        RecordUsage(command);
    }

    // The tokens of Main, before parsing: they hold the token of an unknown command or option, which the model never sees.
    public void RecordArguments(string[]? args)
    {
        _argumentValues = CommandLineValues.FromArguments(args);
    }

    private void RecordUsage(CommandLineApplication command)
    {
        Usage = CommandUsage.Of(command);
        Command = Usage.Command;
        _modelValues = CommandLineValues.Of(command);
    }
}
