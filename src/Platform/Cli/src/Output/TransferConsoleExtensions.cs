using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

public static class TransferConsoleExtensions
{
    // Marks a warning row under the row of its document.
    public const string WarningMark = "!";

    public const string TotalLabel = "Total";

    private static readonly string[] ItemHeaders = ["Kind", "Document", "Outcome", "Code", "Message"];

    /// <summary>
    /// Writes a table of the documents that were not unchanged, with their warnings under them, then the summary per kind,
    /// the total, and the excluded and ignored files.
    /// </summary>
    public static void WriteImport(this IExtendedConsole @this, ViewImport import)
    {
        var items = import.Items ?? [];

        if (items.Count > 0)
        {
            @this.WriteTable(GetItemRows(items), ItemHeaders);
        }

        if (import.AreItemsTruncated)
        {
            @this.WriteLine($"Only the first {items.Count} files are listed; the counts include all.".ThemedLowlight(@this.Theme));
        }

        @this.WriteTable(GetSummaryRows(import), GetSummaryHeaders(import.IsDryRun));

        var total = import.Summary ?? new ViewImportSummary();
        @this.WriteLine($"{Count(total.ExcludedFiles, "file")} excluded, {Count(total.IgnoredFiles, "other file")} ignored.");

        if (import.IsDryRun)
        {
            @this.WriteLine("Nothing was written (dry run).".ThemedLowlight(@this.Theme));
        }
    }

    /// <summary>
    /// One row per document (kind, document, outcome, code, message), followed by one row per warning, marked
    /// <see cref="WarningMark"/> in the outcome column.
    /// </summary>
    public static IReadOnlyList<string[]> GetItemRows(IEnumerable<ViewImportItem> items)
    {
        var rows = new List<string[]>();

        foreach (var item in items)
        {
            rows.Add([FormatKind(item.Kind), FormatDocument(item), item.Outcome.ToString(), item.ErrorCode ?? string.Empty, item.Message ?? string.Empty]);

            foreach (var warning in item.Warnings ?? [])
            {
                rows.Add([string.Empty, string.Empty, WarningMark, warning.Code ?? string.Empty, warning.Message ?? string.Empty]);
            }
        }

        return rows;
    }

    /// <summary>
    /// One row per included kind, in the server's (import) order, then the total.
    /// </summary>
    public static IReadOnlyList<string[]> GetSummaryRows(ViewImport import)
    {
        var rows = (import.Summaries ?? [])
            .Select(summary => GetSummaryRow(FormatContent(summary.Content), summary))
            .ToList();

        rows.Add(GetSummaryRow(TotalLabel, import.Summary ?? new ViewImportSummary()));
        return rows;
    }

    public static string[] GetSummaryHeaders(bool isDryRun)
    {
        return ["Kind", isDryRun ? "Would create" : "Created", isDryRun ? "Would update" : "Updated", "Unchanged", "Skipped", "Failed", "Warnings"];
    }

    // The readable name of a document kind, as the documentation names it.
    public static string FormatKind(ViewImportItemKind kind)
    {
        return kind switch
        {
            ViewImportItemKind.TypeSchema => "Type schema",
            ViewImportItemKind.DescendantTypeSchema => "Descendant-type schema",
            ViewImportItemKind.AnnotationSchema => "Annotation schema",
            _ => kind.ToString(),
        };
    }

    // The key of an annotation, a configuration or an annotation schema; the type code of a template or a type schema; both
    // for a descendant-type schema. The path when the server named neither.
    public static string FormatDocument(ViewImportItem item)
    {
        var hasKey = !string.IsNullOrEmpty(item.AnnotationKey);
        var hasType = !string.IsNullOrEmpty(item.AnnotationType);

        return item.Kind switch
        {
            ViewImportItemKind.DescendantTypeSchema when hasKey && hasType => $"{item.AnnotationType} below {item.AnnotationKey}",
            ViewImportItemKind.Template or ViewImportItemKind.TypeSchema when hasType => item.AnnotationType,
            _ when hasKey => item.AnnotationKey,
            _ when hasType => item.AnnotationType,
            _ => item.Path ?? string.Empty,
        };
    }

    // The kind of a summary, by name: the summary's Content is its own enum in the SDK, the include's is ViewArchiveContent.
    public static string FormatContent(ViewImportSummaryContent? content)
    {
        return content?.ToString() ?? TotalLabel;
    }

    /// <summary>
    /// The counts of the exported kinds, the kinds left out omitted, then the versions and the size, for example
    /// <c>12 annotations, 3 schemas, 2 templates and 8 configurations (20 versions, 4.2 KB)</c>.
    /// </summary>
    public static string FormatExportSummary(ViewExport export)
    {
        var counts = new List<string>();
        AddCount(counts, export.AnnotationCount, "annotation");
        AddCount(counts, export.SchemaCount, "schema");
        AddCount(counts, export.TemplateCount, "template");
        AddCount(counts, export.ConfigurationCount, "configuration");

        var text = counts.Count switch
        {
            0 => "no documents",
            1 => counts[0],
            _ => $"{string.Join(", ", counts.Take(counts.Count - 1))} and {counts[^1]}",
        };

        // only schemas, templates and configurations have versions
        var hasVersions = export.SchemaCount is not null || export.TemplateCount is not null || export.ConfigurationCount is not null;
        var size = FormatSize(export.Size);
        return hasVersions ? $"{text} ({Count(export.VersionCount, "version")}, {size})" : $"{text} ({size})";
    }

    public static string FormatSize(long? bytes)
    {
        return bytes switch
        {
            null => "unknown size",
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
        };
    }

    public static string Count(int? count, string noun)
    {
        var value = count ?? 0;
        return $"{value} {noun}{(value == 1 ? string.Empty : "s")}";
    }

    private static string[] GetSummaryRow(string label, ViewImportSummary summary)
    {
        return
        [
            label,
            Format(summary.Created),
            Format(summary.Updated),
            Format(summary.Unchanged),
            Format(summary.Skipped),
            Format(summary.Failed),
            Format(summary.Warnings),
        ];
    }

    private static void AddCount(List<string> counts, int? count, string noun)
    {
        if (count is not null)
        {
            counts.Add(Count(count, noun));
        }
    }

    private static string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
