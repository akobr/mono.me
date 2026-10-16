using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewArchiveFormat
{
    // Current documents and earlier versions as files, with export.json.
    Folder = 0,

    // A git repository: every version is a commit.
    Git,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewTransferStatus
{
    Queued = 0,
    Running,
    Succeeded,
    Failed,
}

// The kinds of documents an archive holds, in import order; a list of them is the include filter.
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewArchiveContent
{
    Annotations = 0,
    Schemas,
    Templates,
    Configurations,
}

// One document of an archive.
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewDocumentKind
{
    Annotation = 0,
    TypeSchema,
    DescendantTypeSchema,
    AnnotationSchema,
    Template,
    Configuration,
}

// What an import does with a document the view already has.
[JsonConverter(typeof(StringEnumConverter))]
public enum ViewImportExisting
{
    // The file replaces it: a new version, or an update of an annotation.
    Overwrite = 0,

    // It stays as it is; a different file is Skipped with DocumentExists.
    Skip,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewImportOutcome
{
    // The view had no such document (or the annotation was re-created).
    Created = 0,

    // The document changed: a new version was written, or the annotation was updated.
    Updated,

    // Equal to the stored document, nothing was written.
    Unchanged,

    // Not imported on purpose: no annotation, a deleted annotation, an empty document, an existing one kept.
    Skipped,

    // An invalid file, or a document rejected by a schema or a rule.
    Failed,
}

// Body of the export request.
public record class ViewExportCreate
{
    public ViewArchiveFormat Format { get; init; }

    // The kinds to export; null or empty means all four.
    public IReadOnlyList<ViewArchiveContent>? Include { get; init; }
}

// An export of the documents of a view, built in the background.
public record class ViewExport
{
    public required string Id { get; init; }

    public required ViewTransferStatus Status { get; init; }

    public required ViewArchiveFormat Format { get; init; }

    // The exported kinds; null or empty means all four.
    public IReadOnlyList<ViewArchiveContent>? Include { get; init; }

    // The name of the zip, with the creation time in UTC.
    public required string FileName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required AuditActor CreatedBy { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    // The job and its archive are removed after this time.
    public required DateTimeOffset ExpiresAt { get; init; }

    // Exported annotations.
    public int? AnnotationCount { get; init; }

    // Schemas with a current document.
    public int? SchemaCount { get; init; }

    // Templates with a current document.
    public int? TemplateCount { get; init; }

    // Configurations with a current document.
    public int? ConfigurationCount { get; init; }

    // Every exported version of schemas, templates and configurations, the current ones included.
    public int? VersionCount { get; init; }

    // Bytes of the zip.
    public long? Size { get; init; }

    public string? Error { get; init; }
}

// An import of the documents of an archive into a view, processed in the background.
public record class ViewImport
{
    public required string Id { get; init; }

    public required ViewTransferStatus Status { get; init; }

    // The name of the uploaded file, for display.
    public string? FileName { get; init; }

    // The imported kinds; null or empty means all four.
    public IReadOnlyList<ViewArchiveContent>? Include { get; init; }

    // What happens to documents the view already has.
    public ViewImportExisting Existing { get; init; }

    public bool IsDryRun { get; init; }

    public bool IsForced { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required AuditActor CreatedBy { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    // The job is removed after this time.
    public required DateTimeOffset ExpiresAt { get; init; }

    // Document files of the included kinds found in the archive.
    public int? TotalCount { get; init; }

    // Progress while the job runs.
    public int? ProcessedCount { get; init; }

    // The total of every included kind.
    public ViewImportSummary? Summary { get; init; }

    // One summary per included kind, in import order.
    public IReadOnlyList<ViewImportSummary>? Summaries { get; init; }

    // Every outcome except Unchanged, in import order, up to a limit.
    public IReadOnlyList<ViewImportItem>? Items { get; init; }

    public bool AreItemsTruncated { get; init; }

    public string? Error { get; init; }
}

public record class ViewImportSummary
{
    // The kind this summary counts; null on the total.
    public ViewArchiveContent? Content { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Unchanged { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    // Written documents with a warning.
    public int Warnings { get; init; }

    // On the total: files of the kinds that Include leaves out.
    public int ExcludedFiles { get; init; }

    // On the total: version files, export.json and any name that is not a document.
    public int IgnoredFiles { get; init; }
}

public record class ViewImportItem
{
    // The path inside the archive.
    public required string Path { get; init; }

    public required ViewDocumentKind Kind { get; init; }

    // The type code of a template, a type schema or a descendant-type schema.
    public string? AnnotationType { get; init; }

    // The key of an annotation or a configuration, the annotation of an annotation schema, or the ancestor of a
    // descendant-type schema.
    public string? AnnotationKey { get; init; }

    public required ViewImportOutcome Outcome { get; init; }

    // The written version (Created, Updated) or the stored one (Unchanged); none in a dry run and for annotations.
    public ulong? Version { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    // What a written document should be checked for, for example a forced schema violation.
    public IReadOnlyList<ViewImportWarning>? Warnings { get; init; }
}
