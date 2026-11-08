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
    [InlineData("kv export", "Export the key/value pairs of the view.")]
    [InlineData("kv import", "Import key/value pairs into the view.")]
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
    [InlineData("kv export")]
    [InlineData("kv import")]
    public void Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions(string path)
    {
        var command = Command(path);
        var lines = Render(command);
        var detail = HelpTexts.Lines(command.ExtendedHelpText);

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
    [InlineData("kv export")]
    [InlineData("kv import")]
    public void Detail_FitsAnEightyColumnTerminal(string path)
    {
        var detail = HelpTexts.Lines(Command(path).ExtendedHelpText);

        detail.ShouldAllBe(line => line.Length <= DetailWidth);
        detail.ShouldAllBe(line => line == line.TrimEnd());
    }

    [Theory]
    [InlineData("export", "", "-f|--format -i|--include -o|--output -x|--extract --timeout -p|--projectKey -v|--view -?|-h|--help")]
    [InlineData("import", "zip-or-folder", "-i|--include -e|--existing --dry-run --force --timeout -p|--projectKey -v|--view -?|-h|--help")]
    [InlineData("kv export", "", "-o|--output --format --prefix --protection --include-secrets -p|--projectKey -v|--view -?|-h|--help")]
    [InlineData("kv import", "File", "--format --secret --protection --mode --prefix --dry-run -y|--yes -p|--projectKey -v|--view -?|-h|--help")]
    public void ArgumentsAndOptions_StayAsTheyWere(string path, string arguments, string options)
    {
        var command = Command(path);

        command.Arguments.Select(argument => argument.Name).ShouldBe(HelpTexts.Words(arguments));
        command.GetOptions().Select(Template).ShouldBe(HelpTexts.Words(options), ignoreOrder: true);
    }

    // The detail of the key/value commands, from the Files paragraph and the table "Who may do what" of key-values.md.
    [Theory]
    [InlineData(
        "kv export",
        "The formats are json, the 2s.key-values.v1 document with the levels; flat, one JSON object of key: \"value\"; and env, .env lines (KEY=VALUE, with db.password as DB__PASSWORD).",
        "Without --format, the file name decides: env for .env, *.env and .env.*, json for another *.json, and env for any other name; the standard output gets json.",
        "Secrets have a value only with --include-secrets and the secret rights. Without it, json lists them without a value, and flat and env leave them out with a note.",
        "A Reader exports the pairs; the values of secrets need ContributorWithSecrets.",
        "A machine needs Configuration.Read and Configuration.Transfer, and for the values of secrets Secrets.Read and its mTLS certificate, unless the project's policy turns the mTLS rule off.")]
    [InlineData(
        "kv import",
        "The file is json, the 2s.key-values.v1 document with the levels; flat, one JSON object of key: \"value\"; or env, .env lines (KEY=VALUE, with DB__PASSWORD as db.password).",
        "Without --format, the format is guessed from the file name and the content.",
        "flat and env carry no levels: keys matching a --secret glob become secrets of the --protection level, or of the project's default secret level, and the other keys are public.",
        "The plan, a dry run on the server, is shown first and then confirmed: -y skips the confirmation, and --dry-run stops after the plan. A replace that deletes keys asks you to type the view name, unless -y is given. An item that is not valid, such as a new secret without its value, stops the import at the plan: nothing is written and the exit code is 2. The exit code is 45 when the write of an item failed, and 44 when the sign-in or the rights are refused (401 or 403).",
        "A Contributor imports public pairs. Secret values, changes to or from a secret level, and a replace that deletes a secret need ContributorWithSecrets. A machine needs Configuration.Write and Configuration.Transfer, and for secrets Secrets.Write and its mTLS certificate, unless the project's policy turns the mTLS rule off.")]
    public void KeyValueDetail_GivesTheFormatsTheSecretsAndTheRights(string path, params string[] sentences)
    {
        var command = Command(path);
        var detail = HelpTexts.Unwrap(command.ExtendedHelpText);

        // Not for administrators only: Readers export and Contributors import.
        command.Description.ShouldNotBeNull();
        command.Description.ShouldNotContain("administrators");
        detail.ShouldBe(string.Join(' ', sentences));
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

    // The command of sform story at the path, for example "kv export".
    private static CommandLineApplication Command(string path)
    {
        var story = new CommandLineApplication<StorytellerListCommand>();
        story.Conventions.UseDefaultConventions();

        CommandLineApplication command = story;

        foreach (var name in HelpTexts.Words(path))
        {
            command = command.Commands.Single(subcommand => subcommand.Name == name);
        }

        return command;
    }

    private static List<string> Render(CommandLineApplication command)
    {
        using var writer = new StringWriter();
        new FixedWidthHelp().Generate(command, writer);
        return HelpTexts.Lines(writer.ToString());
    }

    private sealed class FixedWidthHelp : DefaultHelpTextGenerator
    {
        public FixedWidthHelp()
        {
            MaxLineLength = Width;
        }
    }
}
