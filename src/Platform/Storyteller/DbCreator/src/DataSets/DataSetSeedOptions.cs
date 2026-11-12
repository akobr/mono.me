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

    // The annotations of annotations.jsonl committed so far in the current set (AnnotationBulkOptions.Progress).
    public IProgress<int>? AnnotationProgress { get; init; }
}
