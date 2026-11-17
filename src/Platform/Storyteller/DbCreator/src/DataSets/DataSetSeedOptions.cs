using System;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

public sealed record class DataSetSeedOptions
{
    // Decide every outcome and write nothing: no account, project, view, document or record.
    public bool DryRun { get; init; }

    // Store schemas and configurations that break each other, with the warning SchemaViolation (ViewImportOptions).
    public bool Force { get; init; }

    // Seed a set whose record says it is seeded already, in the same version and with the same files.
    public bool Again { get; init; }

    // Seed the Public pairs of key-values.json only: the Standard ones are Skipped (PublicPairsOnly), also with a
    // vault, and a set with Standard pairs is not refused without one. Such a set is not recorded, so a seed with a
    // vault adds its Standard pairs later.
    public bool PublicPairsOnly { get; init; }

    // The annotations of annotations.jsonl committed so far in the current set (AnnotationBulkOptions.Progress).
    public IProgress<int>? AnnotationProgress { get; init; }
}
