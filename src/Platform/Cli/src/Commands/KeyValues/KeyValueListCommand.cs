using System;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Subcommand(
    typeof(KeyValueGetCommand),
    typeof(KeyValueSetCommand),
    typeof(KeyValueDeleteCommand),
    typeof(KeyValueRestoreCommand),
    typeof(KeyValuePurgeCommand),
    typeof(KeyValueProtectCommand),
    typeof(KeyValueVersionsCommand),
    typeof(KeyValueDiffCommand),
    typeof(KeyValueExportCommand),
    typeof(KeyValueImportCommand),
    typeof(KeyValueReencryptCommand),
    typeof(KeyValuePolicyCommand),
    typeof(KeyValueRecipientsCommand),
    typeof(KeyValueAuditCommand),
    typeof(KeyValueUsageCommand))]

[Command(CommandNames.KV, CommandNames.KEY_VALUES, Description = "List and manage the key/value pairs of the view, secrets included.")]
public class KeyValueListCommand : KeyValueCommandBase
{
    private readonly TimeProvider _timeProvider;

    public KeyValueListCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, TimeProvider? timeProvider = null)
        : base(console, context, keyValues)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [Option("--prefix", CommandOptionType.SingleValue, Description = "Only keys starting with this prefix.")]
    public string? Prefix { get; set; }

    [Option("--protection", CommandOptionType.MultipleValue, Description = "Only these levels: public, standard, hsm, byok, hyok (repeatable).")]
    public string[]? Protections { get; set; }

    [Option("--values", CommandOptionType.NoValue, Description = "Show the values of public pairs; secrets are masked.")]
    public bool IncludeValues { get; set; }

    [Option("--deleted", CommandOptionType.NoValue, Description = "Only the soft-deleted keys, with their expiry.")]
    public bool Deleted { get; set; }

    [Option("--all", CommandOptionType.NoValue, Description = "Active and soft-deleted keys.")]
    public bool All { get; set; }

    [Option("-k|--continuation-token", CommandOptionType.SingleValue, Description = "The continuation token of the next page.")]
    public string? ContinuationToken { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var levels = Protections is { Length: > 0 }
            ? string.Join(',', Protections.Select(text => KeyValueLevels.Parse(Console, text).ToString()))
            : null;
        var state = All ? "All" : Deleted ? "Deleted" : null;

        var response = await KeyValues.GetKeyValuesAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            Prefix,
            levels,
            state,
            IncludeValues ? true : null,
            ContinuationToken);

        Console.WriteHeader($"Key/value pairs of {Context.OrganizationName}.{Context.ProjectName} / {Context.ViewName}");
        Console.WriteKeyValues(response, IncludeValues, showDeleted: All || Deleted, _timeProvider.GetUtcNow());
        return ExitCodes.SUCCESS;
    }
}
