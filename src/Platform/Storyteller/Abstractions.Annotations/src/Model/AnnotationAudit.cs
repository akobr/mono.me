using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum AnnotationAuditAction
{
    Created = 0,
    Recreated,
    Updated,
    Deleted,
    Restored,
    ConfigurationChanged,
    ConfigurationDeleted,
}

// Why an entry happened without being requested for this key.
[JsonConverter(typeof(StringEnumConverter))]
public enum AnnotationAuditCauseKind
{
    // Deleted or restored together with Key (the root).
    Cascade = 0,

    // Created, re-created or restored because the descendant Key needed it.
    Ancestor,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum AnnotationValueChangeKind
{
    Added = 0,
    Removed,
    Changed,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum AnnotationValueKind
{
    Null = 0,
    String,
    Number,
    Boolean,
    Object,
    Array,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum AnnotationState
{
    Active = 0,
    Deleted,
}

public record class AnnotationAuditCause
{
    public required AnnotationAuditCauseKind Kind { get; init; }

    public string? Key { get; init; }
}

// A changed member of the annotation. Previous and Value are JSON primitives; neither is ever stored for
// Description and UnitDefinition, which record only the fact of the change.
public record class AnnotationFieldChange
{
    public required string Field { get; init; }

    public object? Previous { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPreviousTruncated { get; init; }

    public object? Value { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsValueTruncated { get; init; }
}

public record class AnnotationLabelChanges
{
    public IReadOnlyList<string> Added { get; init; } = [];

    public IReadOnlyList<string> Removed { get; init; } = [];
}

// A changed key of Values. Previous and Value are stored only when their kind is a primitive.
public record class AnnotationValueChange
{
    public required string Key { get; init; }

    public required AnnotationValueChangeKind Change { get; init; }

    public AnnotationValueKind? PreviousKind { get; init; }

    public object? Previous { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPreviousTruncated { get; init; }

    public AnnotationValueKind? ValueKind { get; init; }

    public object? Value { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsValueTruncated { get; init; }
}

public record class AnnotationAuditEntry
{
    // Guid v7 ("N"), time-ordered.
    public required string Id { get; init; }

    public required DateTimeOffset At { get; init; }

    public required AnnotationAuditAction Action { get; init; }

    public required AuditActor Actor { get; init; }

    public AnnotationAuditCause? Cause { get; init; }

    // Deleted and Restored; on Recreated, the deletion it replaced.
    public string? DeletionId { get; init; }

    // Deleted and Restored on the root.
    public int? DescendantCount { get; init; }

    public IReadOnlyList<AnnotationFieldChange>? Fields { get; init; }

    public AnnotationLabelChanges? Labels { get; init; }

    public IReadOnlyList<AnnotationValueChange>? Values { get; init; }

    // ConfigurationChanged: the new version; ConfigurationDeleted: the version that went to history.
    public ulong? ConfigurationVersion { get; init; }
}

public record class AnnotationHistory
{
    public required string AnnotationKey { get; init; }

    public required AnnotationState State { get; init; }

    // Newest first.
    public required IReadOnlyList<AnnotationAuditEntry> Entries { get; init; }

    // Older entries were dropped to keep the audit document within its size limit.
    public bool IsTrimmed { get; init; }
}
