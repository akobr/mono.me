using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _42.Platform.Cli.Commands.Storyteller;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.HelpText;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// The export and import commands of sform story and sform story kv: one line in the list of their parent, and the
// detail at the end of their own help, after the options. Rendered at 80 columns, the width of a redirected output.
public class TransferHelpTests
{
    private const int Width = 80;

    // The detail is written as it is, without wrapping, so it is wrapped by hand for an 80-column terminal.
    private const int DetailWidth = 79;

    [Theory]
    [InlineData("export", "Export the view (for administrators).")]
    [InlineData("import", "Import into the view (for administrators).")]
    public void ParentList_GivesTheCommandOneLine(string path, string description)
    {
        var command = Command(path);
        var lines = Render(command.Parent!);
        var index = lines.FindIndex(line => line.StartsWith($"  {command.Name} ", StringComparison.Ordinal));

        index.ShouldBeGreaterThan(0);
        lines[index][(2 + command.Name!.Length)..].TrimStart().ShouldBe(description);

        // The next command or the end of the list, not a continuation of the description.
        lines[index + 1].ShouldMatch(@"^(  \S.*)?$");
    }

    [Theory]
    [InlineData("export")]
    [InlineData("import")]
    public void Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions(string path)
    {
        var command = Command(path);
        var lines = Render(command);
        var detail = Lines(command.ExtendedHelpText);

        lines[0].ShouldBe(command.Description);
        lines[1].ShouldBeEmpty();
        detail.Count.ShouldBeGreaterThan(1);
        detail[0].ShouldBeEmpty();
        lines.TakeLast(detail.Count).ShouldBe(detail);
        lines[^(detail.Count + 1)].TrimStart().ShouldStartWith("-?|-h|--help");
    }

    [Theory]
    [InlineData("export")]
    [InlineData("import")]
    public void Detail_FitsAnEightyColumnTerminal(string path)
    {
        var detail = Lines(Command(path).ExtendedHelpText);

        detail.ShouldAllBe(line => line.Length <= DetailWidth);
        detail.ShouldAllBe(line => line == line.TrimEnd());
    }

    [Theory]
    [InlineData("export", "", "-f|--format -i|--include -o|--output -x|--extract --timeout -p|--projectKey -v|--view -?|-h|--help")]
    [InlineData("import", "zip-or-folder", "-i|--include -e|--existing --dry-run --force --timeout -p|--projectKey -v|--view -?|-h|--help")]
    public void ArgumentsAndOptions_StayAsTheyWere(string path, string arguments, string options)
    {
        var command = Command(path);

        command.Arguments.Select(argument => argument.Name).ShouldBe(Words(arguments));
        command.GetOptions().Select(Template).ShouldBe(Words(options), ignoreOrder: true);
    }

    // As the help writes it: -?|-h|--help.
    private static string Template(CommandOption option)
    {
        string?[] names =
        [
            option.SymbolName is null ? null : $"-{option.SymbolName}",
            option.ShortName is null ? null : $"-{option.ShortName}",
            option.LongName is null ? null : $"--{option.LongName}",
        ];

        return string.Join('|', names.Where(name => name is not null));
    }

    // The text as one line: the words of every line, separated by single spaces.
    internal static string Unwrap(string? text)
    {
        return string.Join(' ', Words(text));
    }

    private static string[] Words(string? text)
    {
        return (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    // The command of sform story at the path, for example "kv export".
    private static CommandLineApplication Command(string path)
    {
        var story = new CommandLineApplication<StorytellerListCommand>();
        story.Conventions.UseDefaultConventions();

        CommandLineApplication command = story;

        foreach (var name in Words(path))
        {
            command = command.Commands.Single(subcommand => subcommand.Name == name);
        }

        return command;
    }

    private static List<string> Render(CommandLineApplication command)
    {
        using var writer = new StringWriter();
        new FixedWidthHelp().Generate(command, writer);
        return Lines(writer.ToString());
    }

    // Lines without their ends, and without the empty one after a last line end.
    private static List<string> Lines(string? text)
    {
        var lines = (text ?? string.Empty).ReplaceLineEndings("\n").Split('\n').ToList();

        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private sealed class FixedWidthHelp : DefaultHelpTextGenerator
    {
        public FixedWidthHelp()
        {
            MaxLineLength = Width;
        }
    }
}
