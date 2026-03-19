using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using Spectre.Console;

namespace _42.CLI.Toolkit;

public static class CommandParsingExceptionExtensions
{
    public static void WriteOutput(this UnrecognizedCommandParsingException parsingException, bool renderJoke = true)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(parsingException.Message)}[/]");
        var similarCommands = parsingException.NearestMatches.ToList();

        if (similarCommands.Count <= 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(similarCommands.Count == 1
            ? "The most similar command/argument is"
            : "The most similar commands/arguments are");

        foreach (var similarCommand in similarCommands)
        {
            AnsiConsole.Write("    > ");
            AnsiConsole.MarkupLine($"[magenta]{Markup.Escape(similarCommand)}[/]");
        }

        if (renderJoke)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]What happened, {Markup.Escape(ParsingErrorResponseMessages.GetRandom())}?[/]");
        }
    }
}
