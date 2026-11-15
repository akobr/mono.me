using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.HelpText;

namespace _42.Platform.Cli.UnitTests.Commands;

// Help texts as the tests compare them: by lines, or as one line of words. The help is rendered at 80 columns, the
// width of a redirected output.
internal static class HelpTexts
{
    public const int Width = 80;

    // The text as one line: the words of every line, separated by single spaces.
    public static string Unwrap(string? text)
    {
        return string.Join(' ', Words(text));
    }

    public static string[] Words(string? text)
    {
        return (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    // Lines without their ends, and without the empty one after a last line end.
    public static List<string> Lines(string? text)
    {
        var lines = (text ?? string.Empty).ReplaceLineEndings("\n").Split('\n').ToList();

        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    // The command below TRoot at the path, for example "kv export"; a word is a name or an alias. An empty path is
    // the root itself.
    public static CommandLineApplication Command<TRoot>(string path)
        where TRoot : class
    {
        var root = new CommandLineApplication<TRoot>();
        root.Conventions.UseDefaultConventions();

        CommandLineApplication command = root;

        foreach (var name in Words(path))
        {
            command = command.Commands.Single(subcommand => subcommand.Names.Contains(name, StringComparer.Ordinal));
        }

        return command;
    }

    public static List<string> Render(CommandLineApplication command)
    {
        using var writer = new StringWriter();
        new FixedWidthHelp().Generate(command, writer);
        return Lines(writer.ToString());
    }

    private sealed class FixedWidthHelp : DefaultHelpTextGenerator
    {
        public FixedWidthHelp()
        {
            MaxLineLength = Width;
        }
    }
}
