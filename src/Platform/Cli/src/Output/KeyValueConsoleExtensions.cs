using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Output;

// Tables and messages of the key/value commands. Secret values are never printed by a listing.
public static class KeyValueConsoleExtensions
{
    internal const string MtlsRequiredHint = "This project delivers secrets to machines only over mTLS: call with a machine certificate, or sign in as a user with the ContributorWithSecrets role.";

    public static void WriteKeyValues(this IExtendedConsole @this, KeyValuesResponse response, bool includeValues, bool showDeleted, DateTimeOffset now)
    {
        var items = response.Items ?? [];

        if (items.Count == 0)
        {
            @this.WriteLine("No keys found.".ThemedLowlight(@this.Theme));
            return;
        }

        var headers = new List<string> { "Key", "Level" };

        if (includeValues)
        {
            headers.Add("Value");
        }

        headers.AddRange(["Version", "Author", "Updated"]);

        if (showDeleted)
        {
            headers.Add("Deleted");
        }

        @this.WriteTable(
            items,
            item =>
            {
                var cells = new List<string> { item.Key, item.Protection.ToString() };

                if (includeValues)
                {
                    cells.Add(KeyValueLevels.IsSecret(item.Protection) ? KeyValueLevels.Mask : item.IsReference ? $"{item.Value} (reference)" : item.Value ?? string.Empty);
                }

                cells.AddRange([item.Version.ToString(CultureInfo.InvariantCulture), AuditConsoleExtensions.FormatActor(item.Modified?.By), KeyValueTimes.Format(item.Modified?.At)]);

                if (showDeleted)
                {
                    cells.Add(item.IsDeleted ? KeyValueTimes.FormatExpiry(item.ExpiresAt, now) : string.Empty);
                }

                return cells;
            },
            headers);

        if (!string.IsNullOrEmpty(response.ContinuationToken))
        {
            @this.WriteLine();
            @this.WriteLine("More keys follow; next page with ".ThemedLowlight(@this.Theme), $"-k {response.ContinuationToken}");
        }
    }

    public static void WriteKeyValueVersions(this IExtendedConsole @this, IEnumerable<KeyValueVersion> versions)
    {
        @this.WriteTable(
            versions.OrderByDescending(version => version.Version),
            version => new[]
            {
                version.Version.ToString(CultureInfo.InvariantCulture) + (version.IsCurrent ? " (current)" : string.Empty),
                version.Protection.ToString(),
                AuditConsoleExtensions.FormatActor(version.Modified?.By),
                KeyValueTimes.Format(version.Modified?.At),
                version.IsCurrent ? "—" : KeyValueTimes.Format(version.ExpirationTime),
            },
            new[] { "Version", "Level", "Author", "Created", "Expires" });
    }

    // The outcome per key; the exit code is a warning when any key was not done.
    public static int WriteBulkResult(this IExtendedConsole @this, KeyValueBulkResult result, string operation)
    {
        var items = result.Items ?? [];

        if (items.Count == 0)
        {
            @this.WriteLine($"Nothing to {operation}.".ThemedLowlight(@this.Theme));
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        @this.WriteTable(
            items,
            item => new[]
            {
                item.Key,
                item.Outcome.ToString(),
                item.Version?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.Message ?? item.ErrorCode ?? string.Empty,
            },
            new[] { "Key", "Outcome", "Version", "Note" });

        var done = items.Count(item => item.Outcome is KeyValueBulkItemResultOutcome.Done or KeyValueBulkItemResultOutcome.Unchanged);
        var past = operation.EndsWith('e') ? $"{operation}d" : $"{operation}ed";
        @this.WriteLine();

        if (done == items.Count)
        {
            @this.WriteImportant($"{done} of {items.Count} keys {past}.");
            return ExitCodes.SUCCESS;
        }

        @this.WriteImportant($"{done} of {items.Count} keys {past}; the others were not found, not allowed or failed.");
        return ExitCodes.WARNING_PARTIAL_RESULT;
    }

    public static int WriteImportResult(this IExtendedConsole @this, KeyValueImportResult result)
    {
        var items = result.Items ?? [];

        if (items.Count == 0)
        {
            @this.WriteLine("The document has no items.".ThemedLowlight(@this.Theme));
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        @this.WriteTable(
            items,
            item => new[]
            {
                item.Key,
                item.Protection.ToString(),
                item.Action.ToString(),
                item.Version?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.Message ?? item.ErrorCode ?? string.Empty,
            },
            new[] { "Key", "Level", "Action", "Version", "Note" });

        var counts = items.GroupBy(item => item.Action).OrderBy(group => group.Key).Select(group => $"{group.Count()} {group.Key.ToString().ToLowerInvariant()}");
        @this.WriteLine();
        @this.WriteImportant((result.IsDryRun ? "Plan: " : "Imported: ") + string.Join(", ", counts) + ".");

        return result.IsComplete ? ExitCodes.SUCCESS : ExitCodes.WARNING_PARTIAL_RESULT;
    }

    public static void WriteRecipients(this IExtendedConsole @this, IEnumerable<ClientEncryptionRecipient> recipients)
    {
        var list = recipients.ToList();

        if (list.Count == 0)
        {
            @this.WriteLine("The project has no recipients; add one with 'recipients add'.".ThemedLowlight(@this.Theme));
            return;
        }

        @this.WriteTable(
            list,
            recipient => new[]
            {
                recipient.Id,
                recipient.Name,
                recipient.IsEnabled ? "enabled" : "disabled",
                $"RSA {ModulusBits(recipient.PublicKey)} bits",
                KeyValueTimes.Format(recipient.Created?.At),
                AuditConsoleExtensions.FormatActor(recipient.Created?.By),
            },
            new[] { "Id", "Name", "State", "Key", "Added", "By" });
    }

    public static void WriteAudit(this IExtendedConsole @this, KeyValueAuditResponse response)
    {
        var items = response.Items ?? [];

        if (items.Count == 0)
        {
            @this.WriteLine("No audit events.".ThemedLowlight(@this.Theme));
            return;
        }

        @this.WriteTable(
            items,
            item => new[]
            {
                KeyValueTimes.Format(item.Time),
                item.Operation.ToString() + (item.Count > 1 ? $" ×{item.Count}" : string.Empty),
                string.IsNullOrEmpty(item.Key) ? item.View ?? string.Empty : $"{item.View}/{item.Key}" + (item.Version is null ? string.Empty : $"@{item.Version}"),
                item.Protection?.ToString() ?? string.Empty,
                AuditConsoleExtensions.FormatActor(item.Actor),
                item.Channel.ToString(),
                item.Detail ?? string.Empty,
            },
            new[] { "Time", "Operation", "Key", "Level", "Actor", "Channel", "Detail" });

        if (!string.IsNullOrEmpty(response.ContinuationToken))
        {
            @this.WriteLine();
            @this.WriteLine("More events follow; next page with ".ThemedLowlight(@this.Theme), $"-k {response.ContinuationToken}");
        }
    }

    public static void WriteUsage(this IExtendedConsole @this, KeyValueUsageReport report)
    {
        @this.WriteHeader($"Key/value usage {report.From:yyyy-MM-dd} to {report.To:yyyy-MM-dd} ({report.Days?.Count ?? 0} days with data)");

        var totals = report.Totals ?? new Dictionary<string, double>();

        if (totals.Count == 0)
        {
            @this.WriteLine("No usage recorded.".ThemedLowlight(@this.Theme));
            return;
        }

        @this.WriteTable(
            totals.OrderBy(entry => entry.Key, StringComparer.Ordinal),
            entry => new[] { entry.Key, entry.Value.ToString(entry.Key.StartsWith("keys.", StringComparison.Ordinal) ? "0.##" : "0.##", CultureInfo.InvariantCulture) },
            new[] { "Meter", "Total (keys as key-months)" });

        if (report.EstimatedCharges is { Count: > 0 })
        {
            @this.WriteLine();
            @this.WriteTable(
                report.EstimatedCharges.OrderBy(entry => entry.Key, StringComparer.Ordinal),
                entry => new[] { entry.Key, entry.Value.ToString("0.00", CultureInfo.InvariantCulture) },
                new[] { "Priced item", "Estimated charge" });
        }
    }

    // The message and the code of an API error, and the exit code of the command.
    public static int WriteApiError(this IExtendedConsole @this, ApiException exception)
    {
        var (message, errorCode) = ReadError(exception);
        var status = (HttpStatusCode)exception.StatusCode;

        @this.WriteImportant(MessageOf(message, status));

        if (!string.IsNullOrEmpty(errorCode))
        {
            @this.WriteLine($"Error code: {errorCode}".ThemedLowlight(@this.Theme));
        }

        if (string.Equals(errorCode, "MtlsRequired", StringComparison.Ordinal))
        {
            @this.WriteLine(MtlsRequiredHint);
        }

        return ExitCodeOf(status);
    }

    // The line of a failed API call: the server's message, else for a 401 or a 403 the general sentence that Program.Main
    // prints for a refusal (ApiRefusal), and for any other status the status itself.
    internal static string MessageOf(string? message, HttpStatusCode status)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? ApiRefusal.GeneralMessage
            : $"The request failed with {(int)status} {status}.";
    }

    // The exit code of a failed API call, the same whatever writes the message: a refusal of the caller (401, 403) is
    // 44; a status that a retry may change (408, 429, and every 5xx but 501 and 505) is a failure of the service, 3; any
    // other 4xx is a request the API rejects (400, 404, 409, 412, 413, 422, …), 2, and so are 501 (a deployment without
    // the feature, such as ProtectionNotConfigured) and 505, which no retry changes (the transient statuses of the
    // conformance suite, docs/Platform/Storyteller/conformance.md). Only a status that is no error at all (a 2xx or 3xx
    // the client could not read) is a crash of the tool, 1.
    internal static int ExitCodeOf(HttpStatusCode status)
    {
        return (int)status switch
        {
            401 or 403 => ExitCodes.WARNING_UNAUTHORIZED_ACCESS,
            501 or 505 => ExitCodes.ERROR_WRONG_INPUT,
            408 or 429 or (>= 500 and < 600) => ExitCodes.ERROR_SERVICE_FAILURE,
            >= 400 and < 500 => ExitCodes.ERROR_WRONG_INPUT,
            _ => ExitCodes.ERROR_CRASH,
        };
    }

    internal static (string? Message, string? ErrorCode) ReadError(ApiException exception)
    {
        // A status the operation declares: the client has read the body into an ErrorResponse and kept no text.
        if (exception is ApiException<ErrorResponse> { Result: { } error })
        {
            return (error.Message, error.ErrorCode);
        }

        // A 409 of a configuration or schema write, which the client reads as a SchemaValidationErrorResponse whatever
        // the server sent (a concurrent change, a deleted annotation): its message and code.
        if (exception is ApiException<SchemaValidationErrorResponse> { Result: { } validation })
        {
            return (validation.Message, validation.ErrorCode);
        }

        if (string.IsNullOrWhiteSpace(exception.Response))
        {
            return (null, null);
        }

        try
        {
            var body = JObject.Parse(exception.Response);
            return (body["Message"]?.Value<string>(), body["ErrorCode"]?.Value<string>());
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return (exception.Response.Length > 200 ? exception.Response[..200] : exception.Response, null);
        }
    }

    private static int ModulusBits(RsaJsonWebKey? key)
    {
        if (string.IsNullOrEmpty(key?.N))
        {
            return 0;
        }

        try
        {
            return System.Buffers.Text.Base64Url.DecodeFromChars(key.N).Length * 8;
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}
