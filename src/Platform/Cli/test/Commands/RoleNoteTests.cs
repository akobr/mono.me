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

        // The 11 notes of 424-222, those of story export and import, and the 21 of 424-275.
        notes.ShouldBeGreaterThanOrEqualTo(34);
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
    [InlineData("story kv protect", "Re-protect keys (for contributors with secrets).")]
    [InlineData("story kv reencrypt", "Re-encrypt hyok data (for contributors with secrets).")]
    [InlineData("story kv policy byok-setup", "How to bring your own key (for administrators).")]
    [InlineData("story kv policy byok-verify", "Verify the customer key (for administrators).")]
    [InlineData("story kv policy rotate", "Rotate a key-encryption key (for administrators).")]
    [InlineData("story kv recipients add", "Register a recipient (for administrators).")]
    [InlineData("story kv recipients enable", "Enable a recipient (for administrators).")]
    [InlineData("story kv recipients disable", "Disable a recipient (for administrators).")]
    [InlineData("story kv recipients remove", "Remove a recipient (for administrators).")]
    [InlineData("account points grant", "Grant access to point to another user (for administrators).")]
    [InlineData("account points revoke", "Revoke access to point to another user (for administrators).")]
    [InlineData("account shared-cert", "List and manage shared certificates (for administrators).")]
    [InlineData("account shared-cert issue", "Issue a new shared certificate (for administrators).")]
    [InlineData("account shared-cert revoke", "Revoke a shared certificate (for administrators).")]
    [InlineData("account machine create", "Create new machine access (for contributors).")]
    [InlineData("account machine reset", "Reset secret for a machine access (for contributors).")]
    [InlineData("account machine delete", "Delete a machine access (for contributors).")]
    [InlineData("account machine machine-auth", "Show or set the authentication policy (for contributors).")]
    [InlineData("account machine rules", "Show or replace the federation rules (for contributors).")]
    public void ParentList_GivesTheCommandOneLine(string path, string description)
    {
        var command = HelpTexts.Command<PlatformCommand>(path);
        var lines = HelpTexts.Render(command.Parent!);
        var index = lines.FindIndex(line => line.StartsWith($"  {command.Name} ", StringComparison.Ordinal));

        command.Description.ShouldBe(description);
        index.ShouldBeGreaterThan(0);
        lines[index][(2 + command.Name!.Length)..].TrimStart().ShouldBe(description);

        // The next command or the end of the list, not a continuation of the description.
        (index + 1 < lines.Count ? lines[index + 1] : string.Empty).ShouldMatch(@"^(  \S.*)?$");
    }

    // A line that ends inside a note, also right after "(for"; "(for example" is not a note.
    [Theory]
    [InlineData("  --permanent   Delete permanently (for", true)]
    [InlineData("  --permanent   Delete permanently (for administrators", true)]
    [InlineData("  --permanent   Delete permanently (for administrators).", false)]
    [InlineData("  -x            A path (for example", false)]
    public void SplitNote_FindsALineThatEndsInsideANote(string line, bool split)
    {
        SplitNote().IsMatch(line).ShouldBe(split);
    }

    [Theory]
    [InlineData("story kv audit")]
    [InlineData("story kv usage")]
    [InlineData("story kv purge")]
    [InlineData("story kv policy set")]
    [InlineData("account machine issuer set")]
    [InlineData("account machine issuer delete")]
    [InlineData("story kv protect")]
    [InlineData("story kv reencrypt")]
    [InlineData("story kv policy byok-setup")]
    [InlineData("story kv policy byok-verify")]
    [InlineData("story kv policy rotate")]
    [InlineData("story kv recipients add")]
    [InlineData("story kv recipients disable")]
    [InlineData("story kv recipients remove")]
    [InlineData("account points grant")]
    [InlineData("account points revoke")]
    [InlineData("account machine reset")]
    [InlineData("account machine delete")]
    [InlineData("account machine machine-auth")]
    [InlineData("account machine rules")]
    [InlineData("account points create")]
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
    [InlineData("story kv policy byok-setup", "A machine cannot read the setup, whatever its scopes.")]
    [InlineData("story kv policy byok-verify", "A machine cannot verify the key, whatever its scopes.")]
    [InlineData("story kv policy rotate", "A machine cannot rotate a key, whatever its scopes.")]
    [InlineData("story kv recipients add", "A machine cannot change the recipients, whatever its scopes.")]
    [InlineData("story kv recipients disable", "A machine cannot change the recipients, whatever its scopes.")]
    [InlineData("story kv recipients remove", "A machine cannot change the recipients, whatever its scopes.")]
    public void Detail_EndsWithTheRole_AndThatAMachineCannot(string path, string machine)
    {
        var detail = HelpTexts.Unwrap(HelpTexts.Command<PlatformCommand>(path).ExtendedHelpText);

        detail.ShouldEndWith($"A user needs the Administrator role on the project. {machine}", Case.Sensitive);
    }

    // A note names only the lowest role; where another role passes too, or a machine can, the last paragraph of the
    // detail says which role each case needs, and what a machine needs. points create has no note, as a new
    // organization needs no role, but its detail gives the rule.
    [Theory]
    [InlineData("story kv protect", "A user needs the ContributorWithSecrets role on the project. A machine needs no role but the scopes Configuration.Write, KeyValue.Manage and Secrets.Write, and a certificate (mTLS) when the policy requires it.")]
    [InlineData("story kv reencrypt", "A user needs the ContributorWithSecrets role on the project. A machine needs no role but the scopes Configuration.Read, Configuration.Write, Secrets.Read and Secrets.Write, and a certificate (mTLS) when the policy requires it.")]
    [InlineData("account points grant", "A user needs the Administrator role on the access point, and the Owner role to grant the Owner role. A machine cannot grant access, whatever its scopes.")]
    [InlineData("account points revoke", "A user needs the Administrator role on the access point, and the Owner role to revoke the Owner role. A machine cannot revoke access, whatever its scopes.")]
    [InlineData("account machine reset", "A user needs the Contributor role on the project; for a machine with a secrets scope the ContributorWithSecrets role, and with a management scope the Administrator role. A machine cannot reset a secret, whatever its scopes.")]
    [InlineData("account machine delete", "A user needs the Contributor role on the project; for a machine with a secrets scope the ContributorWithSecrets role, and with a management scope the Administrator role. A machine cannot delete a machine access, whatever its scopes.")]
    [InlineData("account machine machine-auth", "A user needs the Contributor role on the project to show the policy, and the Administrator role to set it. A machine cannot show or set it, whatever its scopes.")]
    [InlineData("account machine rules", "A user needs the Contributor role on the project; to replace the rules of a machine with a secrets scope the ContributorWithSecrets role, and with a management scope the Administrator role. A machine cannot show or replace the rules, whatever its scopes.")]
    [InlineData("account points create", "A new organization is created with the project, and you become the Owner of both. A project in an existing organization needs the Owner role on the organization. A machine cannot create a project, whatever its scopes.")]
    public void Detail_EndsWithTheRoles_InFullSentences(string path, string roles)
    {
        var detail = HelpTexts.Command<PlatformCommand>(path).ExtendedHelpText!.ReplaceLineEndings("\n");
        var paragraphs = detail.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        HelpTexts.Unwrap(paragraphs[^1]).ShouldBe(roles);
    }

    // A group has a note only when it and every command below it need the same role.
    [Theory]
    [InlineData("account shared-cert", "(for administrators)")]
    public void GroupNote_IsTheNoteOfEveryCommandBelowIt(string path, string note)
    {
        var group = HelpTexts.Command<PlatformCommand>(path);

        group.Description.ShouldEndWith($"{note}.", Case.Sensitive);
        group.Commands.ShouldNotBeEmpty();
        group.Commands.ShouldAllBe(command => command.Description!.EndsWith($"{note}.", StringComparison.Ordinal));
    }

    // What the descriptions said before they moved after the options.
    [Theory]
    [InlineData("story kv audit", "newest first", "Without -v the events of every view are listed; -v, --key, --operation, --from and --to narrow them.")]
    [InlineData("story kv usage", "--from to --to, by default from 30 days before today to today (at most 400 days)", "with the estimated charges when the deployment has prices.")]
    [InlineData("story kv purge", "removed with every version before their retention ends, without a way to restore them.", "-y skips it.")]
    [InlineData("story kv policy set", "Only the members given change; the others keep their value.", "--allow lists every allowed level and replaces the stored list")]
    [InlineData("account machine issuer set", "Settings not given are cleared, and the issuer URL (--issuer) of an existing issuer cannot change.", "unless --jwks-uri or --jwks-file gives them.")]
    [InlineData("account machine issuer delete", "The issuer is deleted even while federation rules reference it", "Those rules stop matching within a minute")]
    [InlineData("story kv protect", "move to the server-side level", "(standard, hsm or byok) as a new version each")]
    [InlineData("story kv reencrypt", "The client-encrypted (hyok) values", "an existing recipient's private key", "encrypted again for the current recipients")]
    [InlineData("story kv policy byok-setup", "What the customer needs to bring their own key", "the application to consent to", "the role to grant")]
    [InlineData("story kv policy byok-verify", "The customer key set in the policy", "enabled, RSA 2048+, wrap and unwrap allowed, and a round trip works")]
    [InlineData("story kv policy rotate", "The project's standard or hsm key-encryption key", "rotated now, and the re-wrap of the stored values starts")]
    [InlineData("story kv recipients add", "a name (--name) and an RSA public key (3072 bits or more) as a PEM or JWK file, or inline")]
    [InlineData("story kv recipients disable", "New values are not encrypted for a disabled recipient")]
    [InlineData("story kv recipients remove", "Values already encrypted for it stay readable with its private key until they are encrypted again")]
    [InlineData("account machine machine-auth", "the machine authentication policy of the project")]
    [InlineData("account machine rules", "the rules of the federated machine")]
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
    [InlineData(
        "account machine machine-auth",
        "credential-kind",
        "An enabled credential kind (for administrators). One of ApiKey, Certificate, CertificateAndApiKey, ClientCredentials, or Federated; repeat the option or separate the kinds by commas. The kinds given replace the enabled ones.")]
    [InlineData("story kv set", "secret", "A secret (for contributors with secrets). It gets the project's default secret level unless --protection is given.")]
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
    [GeneratedRegex(@"\((Reader|Contributor|ContributorWithSecrets|Administrator|Owner)\b", RegexOptions.None)]
    private static partial Regex RoleInParentheses();

    // A note, but not "(for example …)".
    [GeneratedRegex(@"\(for (?!example\b)[^)]*\)")]
    private static partial Regex Note();

    // A note whose closing parenthesis is on the next line.
    [GeneratedRegex(@"\(for(?: (?!example\b)[^)]*)?$")]
    private static partial Regex SplitNote();
}
