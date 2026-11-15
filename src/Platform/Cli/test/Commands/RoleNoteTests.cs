using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using _42.Platform.Cli.Commands;
using McMaster.Extensions.CommandLineUtils;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// One style for the role a command or an option needs: a note that ends the first sentence of its description, such
// as "(for administrators)", naming the lowest role. A long description keeps one line in the list of its parent and
// gives the rest, with the role in full sentences, after the options of its own help.
public partial class RoleNoteTests
{
    // The detail is written as it is, without wrapping, so it is wrapped by hand for an 80-column terminal.
    private const int DetailWidth = 79;

    private static readonly string[] Notes =
    [
        "(for readers)",
        "(for contributors)",
        "(for contributors with secrets)",
        "(for administrators)",
        "(for owners)",
    ];

    [Fact]
    public void EveryDescription_NamesItsRole_InTheNoteStyle()
    {
        var descriptions = Descriptions(HelpTexts.Command<PlatformCommand>(string.Empty), "sform").ToList();
        var wrong = new List<string>();
        var notes = 0;

        foreach (var (source, text) in descriptions)
        {
            if (RoleInParentheses().IsMatch(text))
            {
                wrong.Add($"{source}: a role in parentheses: {text}");
            }

            foreach (Match note in Note().Matches(text))
            {
                notes++;

                if (!Notes.Contains(note.Value))
                {
                    wrong.Add($"{source}: an unknown note {note.Value}: {text}");
                }

                if (!text[(note.Index + note.Length)..].StartsWith('.'))
                {
                    wrong.Add($"{source}: the note does not end a sentence: {text}");
                }

                if (text[..note.Index].Contains(". "))
                {
                    wrong.Add($"{source}: the note is not in the first sentence: {text}");
                }
            }
        }

        descriptions.Count.ShouldBeGreaterThan(100);
        notes.ShouldBeGreaterThanOrEqualTo(13);
        wrong.ShouldBeEmpty();
    }

    // McMaster wraps the descriptions at a space, so a note could break after "(for".
    [Fact]
    public void NoNote_BreaksAcrossLines_InAnyHelp()
    {
        var broken = new List<string>();

        foreach (var (path, command) in Commands(HelpTexts.Command<PlatformCommand>(string.Empty), "sform"))
        {
            broken.AddRange(HelpTexts.Render(command)
                .Where(line => SplitNote().IsMatch(line))
                .Select(line => $"{path}: {line.Trim()}"));
        }

        broken.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("story export", "Export the view (for administrators).")]
    [InlineData("story import", "Import into the view (for administrators).")]
    [InlineData("story kv audit", "The audit trail of secrets (for administrators).")]
    [InlineData("story kv usage", "The project's key/value usage (for administrators).")]
    [InlineData("story kv purge", "Purge soft-deleted keys (for administrators).")]
    [InlineData("story kv policy set", "Change the policy (for administrators).")]
    [InlineData("account machine issuer list", "List the trusted issuers of the project (for contributors).")]
    [InlineData("account machine issuer set", "Create or replace a trusted issuer (for administrators).")]
    [InlineData("account machine issuer delete", "Delete a trusted issuer (for administrators).")]
    [InlineData("account points get", "Get an access point with its users (for administrators).")]
    public void ParentList_GivesTheCommandOneLine(string path, string description)
    {
        var command = HelpTexts.Command<PlatformCommand>(path);
        var lines = HelpTexts.Render(command.Parent!);
        var index = lines.FindIndex(line => line.StartsWith($"  {command.Name} ", StringComparison.Ordinal));

        command.Description.ShouldBe(description);
        index.ShouldBeGreaterThan(0);
        lines[index][(2 + command.Name!.Length)..].TrimStart().ShouldBe(description);

        // The next command or the end of the list, not a continuation of the description.
        lines[index + 1].ShouldMatch(@"^(  \S.*)?$");
    }

    [Theory]
    [InlineData("story kv audit")]
    [InlineData("story kv usage")]
    [InlineData("story kv purge")]
    [InlineData("story kv policy set")]
    [InlineData("account machine issuer set")]
    [InlineData("account machine issuer delete")]
    public void Help_StartsWithTheDescription_AndEndsWithTheDetail_AfterTheOptions(string path)
    {
        var command = HelpTexts.Command<PlatformCommand>(path);
        var lines = HelpTexts.Render(command);
        var detail = HelpTexts.Lines(command.ExtendedHelpText);

        lines[0].ShouldBe(command.Description);
        lines[1].ShouldBeEmpty();
        detail.Count.ShouldBeGreaterThan(1);
        detail[0].ShouldBeEmpty();
        lines.TakeLast(detail.Count).ShouldBe(detail);
        lines[^(detail.Count + 1)].TrimStart().ShouldStartWith("-?|-h|--help");
        detail.ShouldAllBe(line => line.Length <= DetailWidth);
        detail.ShouldAllBe(line => line == line.TrimEnd());
    }

    // The detail says the role in full, and that a machine cannot, which the note leaves out.
    [Theory]
    [InlineData("story kv audit", "A machine cannot read the audit trail, whatever its scopes.")]
    [InlineData("story kv usage", "A machine cannot read the usage, whatever its scopes.")]
    [InlineData("story kv purge", "A machine cannot purge, whatever its scopes.")]
    [InlineData("story kv policy set", "A machine cannot change the policy, whatever its scopes.")]
    [InlineData("account machine issuer set", "A machine cannot change the trusted issuers, whatever its scopes.")]
    [InlineData("account machine issuer delete", "A machine cannot delete a trusted issuer, whatever its scopes.")]
    public void Detail_EndsWithTheRole_AndThatAMachineCannot(string path, string machine)
    {
        var detail = HelpTexts.Unwrap(HelpTexts.Command<PlatformCommand>(path).ExtendedHelpText);

        detail.ShouldEndWith($"A user needs the Administrator role on the project. {machine}", Case.Sensitive);
    }

    // What the descriptions said before they moved after the options.
    [Theory]
    [InlineData("story kv audit", "newest first", "Without -v the events of every view are listed; -v, --key, --operation, --from and --to narrow them.")]
    [InlineData("story kv usage", "the last 30 by default, or --from to --to (at most 400 days)", "with the estimated charges when the deployment has prices.")]
    [InlineData("story kv purge", "removed with every version before their retention ends, without a way to restore them.", "-y skips it.")]
    [InlineData("story kv policy set", "Only the members given change; the others keep their value.", "--allow lists every allowed level and replaces the stored list")]
    [InlineData("account machine issuer set", "Settings not given are cleared, and the issuer URL (--issuer) of an existing issuer cannot change.", "unless --jwks-uri or --jwks-file gives them.")]
    [InlineData("account machine issuer delete", "The issuer is deleted even while federation rules reference it", "Those rules stop matching within a minute")]
    public void Detail_KeepsWhatTheDescriptionSaid(string path, params string[] parts)
    {
        var detail = HelpTexts.Unwrap(HelpTexts.Command<PlatformCommand>(path).ExtendedHelpText);

        foreach (var part in parts)
        {
            detail.ShouldContain(part, Case.Sensitive);
        }
    }

    [Theory]
    [InlineData("story kv delete", "permanent", "Remove the key for good (for administrators).")]
    [InlineData("story delete", "permanent", "Delete permanently (for administrators).")]
    [InlineData(
        "account machine create",
        "manage",
        "A management scope to grant (for administrators). One of ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage; repeat it or separate the values with commas. Never part of -r or -w.")]
    public void Option_EndsItsFirstSentenceWithTheNote(string path, string longName, string description)
    {
        var option = HelpTexts.Command<PlatformCommand>(path).Options.Single(option => option.LongName == longName);

        option.Description.ShouldBe(description);
    }

    // The descriptions of a command, its own options and arguments, and every command below it.
    private static IEnumerable<(string Source, string Text)> Descriptions(CommandLineApplication root, string rootPath)
    {
        foreach (var (path, command) in Commands(root, rootPath))
        {
            if (command.Description is { } description)
            {
                yield return (path, description);
            }

            foreach (var option in command.Options.Where(option => option.Description is not null))
            {
                yield return ($"{path} {(option.LongName is { } name ? $"--{name}" : $"-{option.ShortName}")}", option.Description!);
            }

            foreach (var argument in command.Arguments.Where(argument => argument.Description is not null))
            {
                yield return ($"{path} <{argument.Name}>", argument.Description!);
            }
        }
    }

    private static IEnumerable<(string Path, CommandLineApplication Command)> Commands(CommandLineApplication command, string path)
    {
        yield return (path, command);

        foreach (var subcommand in command.Commands)
        {
            foreach (var below in Commands(subcommand, $"{path} {subcommand.Name}"))
            {
                yield return below;
            }
        }
    }

    // A role named right after an opening parenthesis: (Administrator), (Contributor or a higher role), …
    [GeneratedRegex(@"\((Reader|Contributor|ContributorWithSecrets|Administrator|Owner)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RoleInParentheses();

    // A note, but not "(for example …)".
    [GeneratedRegex(@"\(for (?!example\b)[^)]*\)")]
    private static partial Regex Note();

    // A note whose closing parenthesis is on the next line.
    [GeneratedRegex(@"\(for (?!example\b)[^)]*$")]
    private static partial Regex SplitNote();
}
