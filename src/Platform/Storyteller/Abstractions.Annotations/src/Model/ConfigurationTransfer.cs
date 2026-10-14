using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationArchiveFormat
{
    // Current documents and earlier versions as files, with export.json.
    Folder = 0,

    // A git repository: every version is a commit.
    Git,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationTransferStatus
{
    Queued = 0,
    Running,
    Succeeded,
    Failed,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationImportOutcome
{
    // The key had no document.
    Created = 0,

    // The document changed, a new version was written.
    Updated,

    // Equal to the stored document, nothing was written.
    Unchanged,

    // Not imported on purpose: no annotation, a deleted annotation, an empty document.
    Skipped,

    // An invalid file, or a document rejected by the schema.
    Failed,
}

// Body of the export request.
public record class ConfigurationExportCreate
{
    public ConfigurationArchiveFormat Format { get; init; }
}

// An export of the configurations of a view, built in the background.
public record class ConfigurationExport
{
    public required string Id { get; init; }

    public required ConfigurationTransferStatus Status { get; init; }

    public required ConfigurationArchiveFormat Format { get; init; }

    // The name of the zip, with the creation time in UTC.
    public required string FileName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required AuditActor CreatedBy { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    // The job and its archive are removed after this time.
    public required DateTimeOffset ExpiresAt { get; init; }

    // Keys with a current document.
    public int? ConfigurationCount { get; init; }

    // Every exported version, the current ones included.
    public int? VersionCount { get; init; }

    // Bytes of the zip.
    public long? Size { get; init; }

    public string? Error { get; init; }
}

// An import of configuration documents into a view, processed in the background.
public record class ConfigurationImport
{
    public required string Id { get; init; }

    public required ConfigurationTransferStatus Status { get; init; }

    // The name of the uploaded file, for display.
    public string? FileName { get; init; }

    public bool IsDryRun { get; init; }

    public bool IsForced { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required AuditActor CreatedBy { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    // The job is removed after this time.
    public required DateTimeOffset ExpiresAt { get; init; }

    // Configuration files found in the archive.
    public int? TotalCount { get; init; }

    // Progress while the job runs.
    public int? ProcessedCount { get; init; }

    public ConfigurationImportSummary? Summary { get; init; }

    // Every outcome except Unchanged, up to a limit.
    public IReadOnlyList<ConfigurationImportItem>? Items { get; init; }

    public bool AreItemsTruncated { get; init; }

    public string? Error { get; init; }
}

public record class ConfigurationImportSummary
{
    public int Created { get; init; }

    public int Updated { get; init; }

    public int Unchanged { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    // Version files, export.json and any other file.
    public int IgnoredFiles { get; init; }
}

public record class ConfigurationImportItem
{
    // The path inside the archive.
    public required string Path { get; init; }

    public string? AnnotationKey { get; init; }

    public required ConfigurationImportOutcome Outcome { get; init; }

    // The written version (Created, Updated) or the stored one (Unchanged); none in a dry run.
    public ulong? Version { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}
