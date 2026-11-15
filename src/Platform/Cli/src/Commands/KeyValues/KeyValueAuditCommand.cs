using System;
using System.Globalization;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.AUDIT, Description = "The audit trail of secrets (for administrators).", ExtendedHelpText = HelpDetail)]
public class KeyValueAuditCommand : KeyValueCommandBase
{
    // After the options of 'sform story kv audit --help', as it is: the empty first line separates it from them,
    // and no line is longer than 79 characters.
    internal const string HelpDetail = """

        The events of the project's secrets, newest first: who revealed, wrote,
        deleted, restored, exported, imported or re-protected them, who changed the
        policy or the keys, and the aggregated binding reads; never a value. Without
        -v the events of every view are listed; -v, --key, --operation, --from and
        --to narrow them.

        A user needs the Administrator role on the project. A machine cannot read the
        audit trail, whatever its scopes.
        """;

    public KeyValueAuditCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("--key", CommandOptionType.SingleValue, Description = "Only events of this key.")]
    public string? Key { get; set; }

    [Option("--operation", CommandOptionType.SingleValue, Description = "Only this operation (reveal, export, bindingread, create, update, delete, restore, permanentdelete, purge, reprotect, import, policychange, rotate, rewrap, byokverify, byokunavailable, recipientadded, recipientchanged, recipientremoved, projectsettingschange).")]
    public string? Operation { get; set; }

    [Option("--from", CommandOptionType.SingleValue, Description = "Only events at or after this time (ISO 8601).")]
    public string? From { get; set; }

    [Option("--to", CommandOptionType.SingleValue, Description = "Only events before this time (ISO 8601).")]
    public string? To { get; set; }

    [Option("-k|--continuation-token", CommandOptionType.SingleValue, Description = "The continuation token of the next page.")]
    public string? ContinuationToken { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var operation = Operation is null ? null : ParseOperation(Operation);
        var response = await KeyValues.GetKeyValueAuditAsync(
            Context.OrganizationName,
            Context.ProjectName,
            ViewName,
            Key,
            operation,
            ParseTime(From, "--from"),
            ParseTime(To, "--to"),
            ContinuationToken);

        Console.WriteHeader($"Secret audit trail of {Context.OrganizationName}.{Context.ProjectName}");
        Console.WriteAudit(response);
        return ExitCodes.SUCCESS;
    }

    private string ParseOperation(string text)
    {
        if (Enum.TryParse<KeyValueAuditEventOperation>(text.Trim(), ignoreCase: true, out var operation) && Enum.IsDefined(operation))
        {
            return operation.ToString();
        }

        Console.WriteImportant($"Unknown operation '{text}'.");
        throw new WrongInputException();
    }

    private DateTimeOffset? ParseTime(string? text, string option)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
        {
            // the generated client sends the time without its offset ("s" format) and the API assumes UTC, so send UTC
            return time.ToUniversalTime();
        }

        Console.WriteImportant($"{option} '{text}' is not a date or time.");
        throw new WrongInputException();
    }
}

[Command(CommandNames.USAGE, Description = "The project's key/value usage (for administrators).", ExtendedHelpText = HelpDetail)]
public class KeyValueUsageCommand : KeyValueCommandBase
{
    // After the options of 'sform story kv usage --help', as it is: the empty first line separates it from them,
    // and no line is longer than 79 characters.
    internal const string HelpDetail = """

        The daily meters of the project's key/value pairs and their totals over a
        range of days: --from to --to, by default from 30 days before today to today
        (at most 400 days), with the estimated charges when the deployment has prices.

        A user needs the Administrator role on the project. A machine cannot read the
        usage, whatever its scopes.
        """;

    public KeyValueUsageCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("--from", CommandOptionType.SingleValue, Description = "The first day (yyyy-MM-dd).")]
    public string? From { get; set; }

    [Option("--to", CommandOptionType.SingleValue, Description = "The last day (yyyy-MM-dd).")]
    public string? To { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var report = await KeyValues.GetKeyValueUsageAsync(Context.OrganizationName, Context.ProjectName, ParseDay(From, "--from"), ParseDay(To, "--to"));
        Console.WriteUsage(report);
        return ExitCodes.SUCCESS;
    }

    private string? ParseDay(string? text, string option)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (System.DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        Console.WriteImportant($"{option} '{text}' is not a day (yyyy-MM-dd).");
        throw new WrongInputException();
    }
}
