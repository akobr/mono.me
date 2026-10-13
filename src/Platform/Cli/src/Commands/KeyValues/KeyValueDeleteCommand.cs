using System;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Soft-delete a pair (restorable for 90 days), or remove it with every version with --permanent.")]
public class KeyValueDeleteCommand : KeyValueCommandBase
{
    public KeyValueDeleteCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Argument(0, Description = "The key.")]
    public string Key { get; set; } = string.Empty;

    [Option("--permanent", CommandOptionType.NoValue, Description = "Remove the key and every version for good (Administrator).")]
    public bool Permanent { get; set; }

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Do not ask for a confirmation of a permanent delete.")]
    public bool Yes { get; set; }

    [Option("--expected-version", CommandOptionType.SingleValue, Description = "Delete only when the key is at this version.")]
    public long? ExpectedVersion { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var key = RequireKey(Key);

        if (Permanent && !Yes)
        {
            var typed = Console.Input<string>($"Type the key to remove '{key}' with every version for good");

            if (!string.Equals(typed?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Aborted, nothing was removed.");
                return ExitCodes.WARNING_ABORTED;
            }
        }

        await KeyValues.DeleteKeyValueAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            key,
            Permanent ? true : null,
            ExpectedVersion);

        if (Permanent)
        {
            Console.WriteImportant($"'{key}' and every version of it were removed.");
        }
        else
        {
            Console.WriteImportant($"'{key}' was deleted.");
            Console.WriteLine("Bring it back within the retention with: ".ThemedLowlight(Console.Theme), $"sform story kv restore {key}");
        }

        return ExitCodes.SUCCESS;
    }
}
