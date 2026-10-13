using System;
using System.Globalization;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.AUDIT, Description = "The audit trail of the project's secrets, newest first (Administrator). -v filters by view.")]
public class KeyValueAuditCommand : KeyValueCommandBase
{
    public KeyValueAuditCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("--key", CommandOptionType.SingleValue, Description = "Only events of this key.")]
    public string? Key { get; set; }

    [Option("--operation", CommandOptionType.SingleValue, Description = "Only this operation (reveal, export, bindingread, create, update, delete, restore, permanentdelete, purge, reprotect, import, policychange, rotate, rewrap, byokverify, byokunavailable, recipientadded, recipientchanged, recipientremoved).")]
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
            return time;
        }

        Console.WriteImportant($"{option} '{text}' is not a date or time.");
        throw new WrongInputException();
    }
}

[Command(CommandNames.USAGE, Description = "The key/value usage of the project over a range of days (the last 30 by default), with estimated charges when the deployment has prices (Administrator).")]
public class KeyValueUsageCommand : KeyValueCommandBase
{
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
