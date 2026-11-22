using System.Globalization;
using _42.Platform.Storyteller.Notifying;

namespace _42.Platform.Storyteller.Transferring;

// The notification of a job that ended, as the inbox of its view shows it. The texts are English, as the messages of
// the API are, and every number is written with the invariant culture.
public static class ViewTransferNotifications
{
    // The role of the job routes: who may not read a job does not see its end.
    private const AccountRole JobRole = AccountRole.Administrator;

    private const int BytesPerUnit = 1024;

    private static readonly string[] SizeUnits = ["KB", "MB", "GB"];

    // The key and the time of the end make every push of one job the same item. A result expires with its job, which it
    // leads to; a failure is complete in itself and lives as long as any other item. A job that has not ended, or a
    // record without the model of its kind, has nothing to report: ArgumentException.
    public static ViewInboxPush Create(ViewTransferRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.Kind == ViewTransferKind.Export
            ? Create(record, record.Export ?? throw NoModel(record))
            : Create(record, record.Import ?? throw NoModel(record));
    }

    private static ViewInboxPush Create(ViewTransferRecord record, ViewExport export)
    {
        var isFailed = IsFailed(record, export.Status);

        return new ViewInboxPush
        {
            Organization = record.Organization,
            Project = record.Project,
            View = record.View,
            Kind = ViewInboxItemKind.Notification,
            Topic = ViewInboxTopics.ViewExport,
            Level = isFailed ? ViewInboxItemLevel.Error : ViewInboxItemLevel.Success,
            Title = isFailed ? "Export failed" : "Export finished",
            Text = isFailed ? export.Error : GetText(export),
            MinimalRole = JobRole,
            CreatedBy = export.CreatedBy,
            ExpiresAt = isFailed ? null : export.ExpiresAt,
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewExport, Id = record.Id, FileName = export.FileName },
            Key = $"{ViewInboxTopics.ViewExport}.{record.Id}",

            // every writer of an end sets CompletedAt; the earlier times keep a record without it reportable
            CreatedAt = export.CompletedAt ?? export.StartedAt ?? export.CreatedAt,
        };
    }

    private static ViewInboxPush Create(ViewTransferRecord record, ViewImport import)
    {
        var isFailed = IsFailed(record, import.Status);

        // a job that succeeded may still hold files that failed
        var level = isFailed
            ? ViewInboxItemLevel.Error
            : import.Summary is { Failed: > 0 } ? ViewInboxItemLevel.Warning : ViewInboxItemLevel.Success;

        return new ViewInboxPush
        {
            Organization = record.Organization,
            Project = record.Project,
            View = record.View,
            Kind = ViewInboxItemKind.Notification,
            Topic = ViewInboxTopics.ViewImport,
            Level = level,
            Title = GetTitle(level, import.IsDryRun),
            Text = isFailed ? import.Error : GetText(import),
            MinimalRole = JobRole,
            CreatedBy = import.CreatedBy,
            ExpiresAt = isFailed ? null : import.ExpiresAt,
            Job = new ViewInboxJob { Kind = ViewInboxJobKind.ViewImport, Id = record.Id, FileName = import.FileName },
            Key = $"{ViewInboxTopics.ViewImport}.{record.Id}",
            CreatedAt = import.CompletedAt ?? import.StartedAt ?? import.CreatedAt,
        };
    }

    // A dry run is a check: it wrote nothing.
    private static string GetTitle(ViewInboxItemLevel level, bool isDryRun)
    {
        return level switch
        {
            ViewInboxItemLevel.Error => isDryRun ? "Check failed" : "Import failed",
            ViewInboxItemLevel.Warning => isDryRun ? "Check found problems" : "Import finished with failures",
            _ => isDryRun ? "Check finished" : "Import finished",
        };
    }

    // "12 annotations, 3 schemas, 2 templates, 40 configurations, 58 versions, 1.2 MB": the counts of the exported
    // kinds, then the size. A kind that was left out has no count and is not named.
    private static string? GetText(ViewExport export)
    {
        var parts = new List<string>();
        AddCount(export.AnnotationCount, "annotation");
        AddCount(export.SchemaCount, "schema");
        AddCount(export.TemplateCount, "template");
        AddCount(export.ConfigurationCount, "configuration");

        // versions belong to schemas, templates and configurations: an export of annotations alone has none to name
        if (export.SchemaCount is not null || export.TemplateCount is not null || export.ConfigurationCount is not null)
        {
            AddCount(export.VersionCount, "version");
        }

        if (export.Size is { } size)
        {
            parts.Add(FormatSize(size));
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);

        void AddCount(int? count, string noun)
        {
            if (count is { } value)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{value} {noun}{(value == 1 ? string.Empty : "s")}"));
            }
        }
    }

    // "acme.zip: 3 created, 2 updated, 10 unchanged, 1 skipped, 0 failed", without a file name only the counts. A dry
    // run names what a real run would create and update, as the administration UI does.
    private static string? GetText(ViewImport import)
    {
        var fileName = string.IsNullOrEmpty(import.FileName) ? null : import.FileName;

        if (import.Summary is not { } summary)
        {
            return fileName;
        }

        var counts = string.Create(
            CultureInfo.InvariantCulture,
            $"{summary.Created} {(import.IsDryRun ? "to create" : "created")}, {summary.Updated} {(import.IsDryRun ? "to update" : "updated")}, {summary.Unchanged} unchanged, {summary.Skipped} skipped, {summary.Failed} failed");

        return fileName is null ? counts : $"{fileName}: {counts}";
    }

    // "512 B", "1.0 KB", "1.2 MB": one decimal from KB up, and GB for everything above.
    private static string FormatSize(long bytes)
    {
        if (bytes < BytesPerUnit)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        var unit = -1;
        decimal value = bytes;

        // rounded before the next unit is asked for, so 1023.96 KB reads 1.0 MB and never 1024.0 KB
        do
        {
            value /= BytesPerUnit;
            unit++;
        }
        while (Round(value) >= BytesPerUnit && unit < SizeUnits.Length - 1);

        return string.Create(CultureInfo.InvariantCulture, $"{Round(value):0.0} {SizeUnits[unit]}");
    }

    private static decimal Round(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static bool IsFailed(ViewTransferRecord record, ViewTransferStatus status)
    {
        return status switch
        {
            ViewTransferStatus.Failed => true,
            ViewTransferStatus.Succeeded => false,
            _ => throw new ArgumentException($"The {GetName(record)} job '{record.Id}' has not ended; it is {status}.", nameof(record)),
        };
    }

    private static ArgumentException NoModel(ViewTransferRecord record)
    {
        return new ArgumentException($"The {GetName(record)} job '{record.Id}' has no {GetName(record)}.", nameof(record));
    }

    private static string GetName(ViewTransferRecord record) => record.Kind.ToString().ToLowerInvariant();
}
