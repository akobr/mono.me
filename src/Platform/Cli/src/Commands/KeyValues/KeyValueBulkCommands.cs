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

[Command(CommandNames.PURGE, Description = "Remove soft-deleted keys with every version before their retention ends (Administrator).")]
public class KeyValuePurgeCommand : KeyValueBulkCommandBase
{
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

[Command(CommandNames.PROTECT, Description = "Move keys to another server-side level (standard, hsm or byok) as a new version each.")]
public class KeyValueProtectCommand : KeyValueBulkCommandBase
{
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
