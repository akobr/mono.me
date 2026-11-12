using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// An outcome worth a line of its own: a document or an annotation of annotations.jsonl that was skipped, failed or
// updated. Path is below the set's folder; Line is the line of annotations.jsonl (null for a file).
public sealed record class DataSetSeedItem
{
    public required string Path { get; init; }

    public int? Line { get; init; }

    public required ViewDocumentKind Kind { get; init; }

    public string? AnnotationType { get; init; }

    public string? AnnotationKey { get; init; }

    public required ViewImportOutcome Outcome { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<ViewImportWarning>? Warnings { get; init; }
}
