using System;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.RESTORE, Description = "Restore soft-deleted keys to their latest version (keys, or --prefix).")]
public class KeyValueRestoreCommand : KeyValueBulkCommandBase
{
    public KeyValueRestoreCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var request = BuildRequest();
        var result = await KeyValues.RestoreKeyValuesAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, request);
        return Console.WriteBulkResult(result, "restore");
    }
}

[Command(CommandNames.PURGE, Description = "Purge soft-deleted keys (for administrators).", ExtendedHelpText = HelpDetail)]
public class KeyValuePurgeCommand : KeyValueBulkCommandBase
{
    // After the options of 'sform story kv purge --help', as it is: the empty first line separates it from them,
    // and no line is longer than 79 characters.
    internal const string HelpDetail = """

        The keys, given as arguments or by --prefix (an empty prefix means every
        key), are removed with every version before their retention ends, without a
        way to restore them. The purge asks for a confirmation, for a prefix the view
        name; -y skips it.

        A user needs the Administrator role on the project. A machine cannot purge,
        whatever its scopes.
        """;

    public KeyValuePurgeCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Do not ask for a confirmation.")]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var request = BuildRequest();

        if (!Yes)
        {
            Console.EnsureInteractive("the confirmation of the purge", "Pass -y|--yes to purge without asking.");

            if (HasPrefix)
            {
                var typed = Console.Input<string>($"Type the view name '{Context.ViewName}' to purge {DescribeTarget()}");

                if (!string.Equals(typed?.Trim(), Context.ViewName, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Aborted, nothing was purged.");
                    return ExitCodes.WARNING_ABORTED;
                }
            }
            else if (!Console.Confirm($"Purge {DescribeTarget()} with every version for good?", false))
            {
                Console.WriteLine("Aborted, nothing was purged.");
                return ExitCodes.WARNING_ABORTED;
            }
        }

        var result = await KeyValues.PurgeKeyValuesAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, request);
        return Console.WriteBulkResult(result, "purge");
    }
}

[Command(CommandNames.PROTECT, Description = "Re-protect keys (for contributors with secrets).", ExtendedHelpText = HelpDetail)]
public class KeyValueProtectCommand : KeyValueBulkCommandBase
{
    // After the options of 'sform story kv protect --help', as it is: the empty first line separates it from them,
    // and no line is longer than 79 characters.
    internal const string HelpDetail = """

        The keys, given as arguments or by --prefix (an empty prefix means every key),
        move to the server-side level of --protection (standard, hsm or byok) as a new
        version each; older versions keep their level until they expire. Public and
        hyok need a new value from 'sform story kv set'.

        A user needs the ContributorWithSecrets role on the project. A machine needs no
        role but the scopes Configuration.Write, KeyValue.Manage and Secrets.Write, and
        a certificate (mTLS) when the policy requires it.
        """;

    public KeyValueProtectCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("--protection", CommandOptionType.SingleValue, Description = "The target level: standard, hsm or byok.")]
    public string? Protection { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var level = KeyValueLevels.Parse(Console, Protection);

        if (level is KeyValueProtection.Public or KeyValueProtection.Hyok)
        {
            Console.WriteImportant("Only standard, hsm and byok can be reached on the server; public and hyok need a new value with 'set'.");
            throw new WrongInputException();
        }

        var request = BuildRequest();
        request.Protection = KeyValueLevels.To<KeyValueBulkRequestProtection>(level);

        var result = await KeyValues.ProtectKeyValuesAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, request);
        var exitCode = Console.WriteBulkResult(result, "protect");
        Console.WriteLine("Older versions keep their previous level until they expire.".ThemedLowlight(Console.Theme));
        return exitCode;
    }
}
