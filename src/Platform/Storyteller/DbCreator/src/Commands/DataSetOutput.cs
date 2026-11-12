using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using _42.Platform.Storyteller.Transferring;
using Console = Colorful.Console;

namespace _42.Platform.Storyteller.DbCreator.Commands;

// What the commands print about data sets: the plan of a seed, each set with its counts and failures, the end of a
// run, the short summary of the core seed, the listing of the sets, and the errors a command maps to an exit code.
public static class DataSetOutput
{
    // The failures listed per set; the rest are counted.
    public const int MaxListedFailures = 20;

    private static readonly Color Muted = Color.DarkGray;
    private static readonly Color Problem = Color.Magenta;

    // The annotations of annotations.jsonl written so far, a line after each executed plan of the bulk writer.
    public static IProgress<int> AnnotationProgress { get; } = new LineProgress();

    public static void WritePlan(DataSetSeedPlan plan, DataSetSeedOptions options)
    {
        if (options.DryRun)
        {
            Console.WriteLine("Dry run: nothing is written, every outcome is what a seed would do.", Color.Yellow);
        }

        Console.WriteLine(
            $"Seeding {Sets(plan.Sets.Select(set => set.Name).ToList())} into '{plan.Target}' for the owner {plan.Owner.Name} (#{plan.Owner.Id}).");
    }

    // A seed: each set, what the target got (after the progress lines of the sets, so with the end), and the end.
    public static void WriteRun(DataSetRunResult run, TimeSpan elapsed)
    {
        foreach (var set in run.Sets)
        {
            WriteSet(set, run.Target, run.IsDryRun);
        }

        Console.WriteLine();
        WriteProvisioning(run);
        var failed = run.Sets.Where(set => set.HasFailures).Select(set => set.Set.Name).ToList();

        if (failed.Count > 0)
        {
            Console.Write("! ", Problem);
            Console.WriteLine(run.IsDryRun
                ? $"{Capitalize(Sets(failed))} would fail."
                : $"{Capitalize(Sets(failed))} failed and {(failed.Count == 1 ? "is" : "are")} not recorded: a run again seeds what is missing.");
        }

        if (run.IsDryRun)
        {
            Console.WriteLine($"Dry run in {Elapsed(elapsed)}: nothing was written.", Color.Yellow);
        }
        else if (failed.Count == 0)
        {
            var seeded = run.Sets.Count(set => set.IsSeeded);
            var already = run.Sets.Count(set => set.IsAlreadySeeded);
            Console.WriteLine($"Done in {Elapsed(elapsed)}: {Count(seeded, "set")} seeded, {Count(already, "set")} seeded already.", Color.Green);
        }
    }

    // A line per set, with the failures: the summary of the core seed of the root command.
    public static void WriteSummary(DataSetRunResult run)
    {
        foreach (var result in run.Sets)
        {
            var name = result.Set.Name;

            if (result.IsAlreadySeeded)
            {
                Console.WriteLine($"The data set '{name}' is seeded in '{run.Target}' already.");
            }
            else if (result.FailedDependency is { } dependency)
            {
                Console.Write("! ", Problem);
                Console.WriteLine($"The data set '{name}' is not seeded: the set '{dependency}' it depends on failed.");
            }
            else
            {
                var total = Total(result);
                Console.WriteLine(
                    $"The data set '{name}' is seeded into '{run.Target}' in {Elapsed(result.Elapsed)}: {N(total.Created)} created, {N(total.Unchanged)} unchanged, {N(total.Skipped)} skipped, {N(total.Failed)} failed.");
                WriteFailures(result);
            }
        }
    }

    public static void WriteOverview(DataSetOverview overview, IReadOnlyCollection<string> catalog)
    {
        var set = overview.Set;
        var manifest = set.Manifest;

        Console.WriteLine();
        Console.Write(set.Name, Color.Cyan);
        Console.WriteLine($"  version {manifest.Version}, {Source(set)}", Muted);

        if (manifest.Description is not null)
        {
            Console.WriteLine($"  {manifest.Description}");
        }

        var target = $"{manifest.Organization ?? "<organization>"}/{manifest.Project ?? "<project>"}/{manifest.View ?? Storyteller.Constants.DefaultViewName}";
        Console.WriteLine(manifest.Organization is null || manifest.Project is null
            ? $"  Target: {target} (give --organization and --project)"
            : $"  Target: {target}");

        if (manifest.DependsOn.Count > 0)
        {
            Console.WriteLine($"  Depends on: {string.Join(", ", manifest.DependsOn.Select(name => catalog.Contains(name) ? name : $"{name} (not in the catalog)"))}");
        }

        var documents = string.Join(", ", overview.Documents.Select(pair => Count(pair.Count, Name(pair.Content).TrimEnd('s'))));
        var unreadable = overview.FailedDocuments > 0 ? $", {N(overview.FailedDocuments)} of them cannot be read" : string.Empty;
        var ignored = overview.IgnoredFiles > 0 ? $"; {Count(overview.IgnoredFiles, "other file")} ignored" : string.Empty;
        Console.WriteLine($"  Documents: {documents}{unreadable}{ignored}");

        if (overview.Lines is { } lines)
        {
            var failed = overview.FailedAnnotations > 0 ? $", {N(overview.FailedAnnotations)} of them cannot be read" : string.Empty;
            Console.WriteLine(
                $"  {DataSetAnnotationLinesReader.FileName}: {Count(lines.Lines, "line")} ({Count(lines.GeneratorLines, "generator")}), {Count(overview.Annotations, "annotation")}{failed}");
        }

        Console.WriteLine($"  Folder: {set.Folder}", Muted);
    }

    public static void WriteWrongInput(DataSetException exception)
    {
        Console.Write("! ", Problem);
        Console.WriteLine(exception.Message);
    }

    public static void WriteLayoutProblems(DatabaseLayoutException exception)
    {
        Console.Write("! ", Problem);
        Console.WriteLine(
            $"The database '{CosmosConstants.DatabaseName}' does not have the layout of the shared container '{CosmosConstants.OrganizationsContainerName}'. Nothing was written.");

        foreach (var problem in exception.Problems)
        {
            Console.WriteLine($"  {problem}");
        }

        Console.WriteLine(
            $"  Its data is not migrated: delete the database '{CosmosConstants.DatabaseName}' (or start the emulator with a new data volume), then run DbCreator again to seed it.");
    }

    private static void WriteProvisioning(DataSetRunResult run)
    {
        var provisioning = run.Provisioning;
        var created = new List<string>();

        if (provisioning.IsAccountCreated)
        {
            created.Add($"the account #{run.Owner.Id}");
        }

        if (provisioning.IsOrganizationCreated)
        {
            created.Add($"the organization '{run.Target.Organization}'");
        }

        if (provisioning.IsProjectCreated)
        {
            created.Add($"the project '{run.Target.ProjectKey}'");
        }

        if (provisioning.IsViewRegistered)
        {
            created.Add($"the view '{run.Target.View}'");
        }

        if (created.Count > 0)
        {
            Console.WriteLine($"{(run.IsDryRun ? "Would create" : "Created")} {Join(created)}.");
        }

        if (!provisioning.IsProjectCreated)
        {
            Console.WriteLine($"The project '{run.Target.ProjectKey}' exists and is used as it is.", Muted);
        }
    }

    private static void WriteSet(DataSetSeedResult result, DataSetTarget target, bool isDryRun)
    {
        var set = result.Set;

        Console.WriteLine();
        Console.Write($"# {set.Name}", Color.Cyan);
        Console.WriteLine($"  version {set.Manifest.Version}, {Source(set)}", Muted);

        if (result.IsAlreadySeeded)
        {
            Console.WriteLine($"  Seeded in '{target}' already, in this version and with these files: nothing was read (--again seeds it again).");
            return;
        }

        if (result.FailedDependency is { } dependency)
        {
            Console.Write("  ! ", Problem);
            Console.WriteLine($"Not seeded: the set '{dependency}' it depends on failed.");
            return;
        }

        var documents = result.Documents!;

        foreach (var summary in documents.Summaries.Where(HasAny))
        {
            Console.WriteLine($"  {Name(summary.Content!.Value)}: {Counts(summary)}");
        }

        if (result.Lines is { } lines)
        {
            Console.WriteLine(
                $"  {DataSetAnnotationLinesReader.FileName}: {Count(lines.Lines, "line")} ({Count(lines.GeneratorLines, "generator")}), {Count(lines.Annotations, "annotation")}: {Counts(lines.Summary)}");

            if (lines.AncestorsCreated.Count > 0 || lines.RelationUpdates > 0)
            {
                Console.WriteLine(
                    $"    and {Count(lines.AncestorsCreated.Count, "ancestor")} {(isDryRun ? "to create" : "created")}, {Count(lines.RelationUpdates, "relation array")} of existing annotations {(isDryRun ? "to update" : "updated")}",
                    Muted);
            }
        }

        if (result.RelationsCompleted > 0)
        {
            Console.WriteLine(
                $"  The relation arrays of {Count(result.RelationsCompleted, "annotation")} {(isDryRun ? "to complete" : "completed")} with the annotation files an earlier run left out of them",
                Muted);
        }

        if (documents.Total == 0 && result.Lines is null)
        {
            Console.WriteLine("  No documents.", Muted);
        }

        if (documents.Summary.IgnoredFiles > 0)
        {
            Console.WriteLine($"  {Count(documents.Summary.IgnoredFiles, "file")} that {(documents.Summary.IgnoredFiles == 1 ? "is" : "are")} not a document ignored.", Muted);
        }

        WriteFailures(result);

        // the warnings of the documents by code (those of annotations.jsonl are counted only); they never fail a seed
        var warnings = documents.Items
            .SelectMany(item => item.Warnings ?? [])
            .GroupBy(warning => warning.Code, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{N(group.Count())} {group.Key}")
            .ToList();

        if (warnings.Count > 0)
        {
            Console.WriteLine($"  Warnings: {string.Join(", ", warnings)}.", Color.Yellow);
        }

        // the other listed outcomes, by what they are: kept documents, deleted annotations, what hangs on a failure
        var others = result.Items
            .Where(item => item.Outcome != ViewImportOutcome.Failed)
            .GroupBy(item => $"{item.Outcome} {item.ErrorCode}".TrimEnd())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{N(group.Count())} {group.Key}")
            .ToList();

        if (others.Count > 0)
        {
            Console.WriteLine($"  Not created: {string.Join(", ", others)}.", Muted);
        }

        var elapsed = Elapsed(result.Elapsed);
        Console.WriteLine(
            isDryRun ? $"  Checked in {elapsed}." : result.IsRecorded ? $"  Seeded and recorded in {elapsed}." : $"  Seeded in {elapsed}, not recorded.",
            Muted);
    }

    private static void WriteFailures(DataSetSeedResult result)
    {
        var failures = result.Failures.ToList();

        if (failures.Count == 0)
        {
            return;
        }

        Console.Write("  ! ", Problem);
        Console.WriteLine($"{Count(failures.Count, "failure")}:");

        foreach (var item in failures.Take(MaxListedFailures))
        {
            Console.WriteLine($"    {Where(item)}: {item.ErrorCode} {item.Message}".TrimEnd());
        }

        if (failures.Count > MaxListedFailures)
        {
            Console.WriteLine($"    … and {N(failures.Count - MaxListedFailures)} more.", Muted);
        }
    }

    private static string Where(DataSetSeedItem item)
    {
        return item.Line is { } line
            ? $"{item.Path} line {line}{(item.AnnotationKey is null ? string.Empty : $" ({item.AnnotationKey})")}"
            : item.Path;
    }

    private static ViewImportSummary Total(DataSetSeedResult result)
    {
        var documents = result.Documents?.Summary ?? new ViewImportSummary();
        var lines = result.Lines?.Summary ?? new ViewImportSummary();

        return new ViewImportSummary
        {
            Created = documents.Created + lines.Created,
            Updated = documents.Updated + lines.Updated,
            Unchanged = documents.Unchanged + lines.Unchanged,
            Skipped = documents.Skipped + lines.Skipped,
            Failed = documents.Failed + lines.Failed,
            Warnings = documents.Warnings + lines.Warnings,
        };
    }

    private static bool HasAny(ViewImportSummary summary)
    {
        return summary.Created + summary.Updated + summary.Unchanged + summary.Skipped + summary.Failed > 0;
    }

    private static string Counts(ViewImportSummary summary)
    {
        return $"{N(summary.Created)} created, {N(summary.Updated)} updated, {N(summary.Unchanged)} unchanged, {N(summary.Skipped)} skipped, {N(summary.Failed)} failed, {N(summary.Warnings)} with warnings";
    }

    private static string Source(DataSet set)
    {
        return set.Source == DataSetSource.BuiltIn ? "built-in" : "--path";
    }

    private static string Name(ViewArchiveContent content)
    {
        return content.ToString().ToLowerInvariant();
    }

    private static string Sets(IReadOnlyList<string> names)
    {
        return names.Count == 1 ? $"the set '{names[0]}'" : $"the sets {Join(names.Select(name => $"'{name}'").ToList())}";
    }

    private static string Join(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
        };
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Count(int count, string noun)
    {
        return $"{N(count)} {noun}{(count == 1 ? string.Empty : "s")}";
    }

    private static string N(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string Elapsed(TimeSpan elapsed)
    {
        return elapsed.TotalMinutes < 1
            ? $"{elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s"
            : elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
    }

    // Reported after each executed plan, in order, from the seed's own flow: a line each, no redraw.
    private sealed class LineProgress : IProgress<int>
    {
        public void Report(int value)
        {
            Console.WriteLine($"  … {Count(value, "annotation")} of {DataSetAnnotationLinesReader.FileName} written", Muted);
        }
    }
}
