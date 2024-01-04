using System.Drawing;
using System.Linq;
using Colorful;
using McMaster.Extensions.CommandLineUtils;

namespace _42.CLI.Toolkit;

public static class CommandParsingExceptionExtensions
{
    public static void WriteOutput(this UnrecognizedCommandParsingException parsingException, bool renderJoke = true)
    {
        Console.WriteLine(parsingException.Message, Color.DarkGray);
        var similarCommands = parsingException.NearestMatches.ToList();

        if (similarCommands.Count <= 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(similarCommands.Count == 1
            ? "The most similar command/argument is"
            : "The most similar commands/arguments are");
        foreach (var similarCommand in similarCommands)
        {
            Console.Write("    > ");
            Console.WriteLine(similarCommand, Color.Magenta);
        }

        if (renderJoke)
        {
            Console.WriteLine();
            Console.WriteLine($"What happened, {ParsingErrorResponseMessages.GetRandom()}?", Color.DarkGray);
        }
    }
}
