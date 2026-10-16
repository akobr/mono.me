using System.Globalization;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Output;

public static class TransferConsoleExtensions
{
    /// <summary>
    /// Writes the counts of an import and a table of the files that were not unchanged.
    /// </summary>
    public static void WriteImport(this IExtendedConsole @this, ViewImport import)
    {
        var summary = import.Summary ?? new ViewImportSummary();
        @this.WriteLine(
            $"{(import.IsDryRun ? "Would create" : "Created")} {summary.Created}, "
            + $"{(import.IsDryRun ? "would update" : "updated")} {summary.Updated}, "
            + $"unchanged {summary.Unchanged}, skipped {summary.Skipped}, failed {summary.Failed}; "
            + $"{Count(summary.IgnoredFiles, "other file")} ignored.");

        var items = import.Items ?? [];

        if (items.Count > 0)
        {
            @this.WriteLine();
            @this.WriteTable(
                items,
                item => new[]
                {
                    item.Outcome.ToString(),
                    item.AnnotationKey ?? item.Path,
                    item.Version?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    item.Message ?? string.Empty,
                },
                new[] { "Outcome", "Key", "Version", "Message" });
        }

        if (import.AreItemsTruncated)
        {
            @this.WriteLine($"Only the first {items.Count} files are listed; the counts include all.".ThemedLowlight(@this.Theme));
        }

        if (import.IsDryRun)
        {
            @this.WriteLine("Nothing was written (dry run).".ThemedLowlight(@this.Theme));
        }
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
}
