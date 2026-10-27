using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Output;

public static class AuditConsoleExtensions
{
    // Fields whose audit records only that they changed, never a value.
    private static readonly HashSet<string> FlaggedFields = new(StringComparer.OrdinalIgnoreCase) { "Description", "UnitDefinition" };

    // Fields whose audit records an instant. The SDK keeps date-like strings as stored, so they arrive as text.
    private static readonly HashSet<string> InstantFields = new(StringComparer.OrdinalIgnoreCase) { "ValidFrom", "ExpiresAt" };

    /// <summary>
    /// Writes the history of an annotation as a table, the newest entry first.
    /// </summary>
    public static void WriteHistory(this IExtendedConsole @this, AnnotationHistory history, int? top = null)
    {
        var entries = (history.Entries ?? []).OrderByDescending(entry => entry.At).ToList();
        var shown = top is > 0 ? entries.Take(top.Value).ToList() : entries;

        if (history.State == AnnotationHistoryState.Deleted)
        {
            @this.WriteLine($"The annotation '{history.AnnotationKey}' is deleted.".ThemedHighlight(@this.Theme));
        }

        @this.WriteTable(
            shown,
            entry => new[]
            {
                FormatTime(entry.At),
                FormatActor(entry.Actor),
                FormatAction(entry.Action),
                FormatChanges(entry),
            },
            new[] { "When", "Who", "Action", "Changes" });

        if (shown.Count < entries.Count)
        {
            @this.WriteLine($"{entries.Count - shown.Count} older entries are not shown.".ThemedLowlight(@this.Theme));
        }

        if (history.IsTrimmed)
        {
            @this.WriteLine("The oldest entries were dropped to keep the history within its size limit.".ThemedLowlight(@this.Theme));
        }
    }

    /// <summary>
    /// The changes of one audit entry on one line, for example <c>Title: "Old" → "New"; +eu −us; plan: "pro" → "ent"</c>.
    /// </summary>
    public static string FormatChanges(AnnotationAuditEntry entry)
    {
        var parts = new List<string>();

        if (entry.Cause is { } cause)
        {
            parts.Add(cause.Kind == AnnotationAuditCauseKind.Cascade
                ? $"with {cause.Key}"
                : $"ancestor of {cause.Key}");
        }

        if (entry.DescendantCount is > 0)
        {
            parts.Add(entry.DescendantCount == 1 ? "1 descendant" : $"{entry.DescendantCount} descendants");
        }

        parts.AddRange((entry.Fields ?? []).Select(FormatField));

        if (entry.Labels is { } labels)
        {
            var labelChanges = (labels.Added ?? []).Select(label => $"+{label}")
                .Concat((labels.Removed ?? []).Select(label => $"−{label}"))
                .ToList();

            if (labelChanges.Count > 0)
            {
                parts.Add(string.Join(" ", labelChanges));
            }
        }

        parts.AddRange((entry.Values ?? []).Select(FormatValueChange));

        if (entry.ConfigurationVersion is { } version)
        {
            parts.Add($"config v{version.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join("; ", parts);
    }

    public static string FormatActor(AuditActor? actor)
    {
        return actor?.Kind switch
        {
            null => "unknown",
            AuditActorKind.Machine => $"machine:{actor.Id}",
            AuditActorKind.System => "system",
            _ => !string.IsNullOrWhiteSpace(actor.Name) ? actor.Name : actor.Id ?? "unknown",
        };
    }

    public static string FormatAction(AnnotationAuditEntryAction action)
    {
        return action switch
        {
            AnnotationAuditEntryAction.Recreated => "Re-created",
            AnnotationAuditEntryAction.ConfigurationChanged => "Config changed",
            AnnotationAuditEntryAction.ConfigurationDeleted => "Config deleted",
            _ => action.ToString(),
        };
    }

    // "2026-10-11 14:02 by Ales Kobr"
    public static string FormatDeletion(AnnotationDeletion deletion)
    {
        return $"{FormatTime(deletion.DeletedAt)} by {FormatActor(deletion.DeletedBy)}";
    }

    public static string FormatTime(DateTimeOffset time)
    {
        return time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string FormatDate(DateTimeOffset time)
    {
        return time.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string FormatField(AnnotationFieldChange field)
    {
        if (FlaggedFields.Contains(field.Field))
        {
            return $"{field.Field} changed";
        }

        if (InstantFields.Contains(field.Field))
        {
            return $"{field.Field}: {FormatValue(ToInstant(field.Previous), false)} → {FormatValue(ToInstant(field.Value), false)}";
        }

        return $"{field.Field}: {FormatValue(field.Previous, field.IsPreviousTruncated)} → {FormatValue(field.Value, field.IsValueTruncated)}";
    }

    // The stored text of an instant, with its offset kept; one without an offset is UTC, as the platform stores instants.
    private static object? ToInstant(object? value)
    {
        return value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant
            : value;
    }

    private static string FormatValueChange(AnnotationValueChange change)
    {
        return change.Change switch
        {
            AnnotationValueChangeChange.Added => $"+{change.Key}: {FormatValue(ToKind(change.ValueKind), change.Value, change.IsValueTruncated)}",
            AnnotationValueChangeChange.Removed => $"−{change.Key}",
            _ => $"{change.Key}: {FormatValue(ToKind(change.PreviousKind), change.Previous, change.IsPreviousTruncated)}"
                 + $" → {FormatValue(ToKind(change.ValueKind), change.Value, change.IsValueTruncated)}",
        };
    }

    // Objects and arrays are recorded only by their kind.
    private static string FormatValue(string? kind, object? value, bool isTruncated)
    {
        return kind switch
        {
            "Object" => "{…}",
            "Array" => "[…]",
            _ => FormatValue(value, isTruncated),
        };
    }

    private static string FormatValue(object? value, bool isTruncated)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}{(isTruncated ? "…" : string.Empty)}\"",
            bool flag => flag ? "true" : "false",
            // changed timestamps keep seconds and fractions, so two close values stay distinct
            DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) + (time.Kind == DateTimeKind.Utc ? "Z" : string.Empty),
            DateTimeOffset time => time.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture),
            JValue token => FormatValue(token.Value, isTruncated),
            JToken token => token.ToString(Formatting.None),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string? ToKind<TKind>(TKind? kind)
        where TKind : struct, Enum
    {
        return kind?.ToString();
    }
}
