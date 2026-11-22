using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxItemKind
{
    // The result of work that ran in the background.
    Notification = 0,

    // Something the platform suggests; a person accepts or dismisses it.
    Action,

    // A plain text of the platform.
    Message,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxItemState
{
    Open = 0,
    Accepted,
    Dismissed,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxItemLevel
{
    Info = 0,
    Success,
    Warning,
    Error,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewInboxJobKind
{
    ViewExport = 0,
    ViewImport,
}

// The background job an item reports, or the one its acceptance started.
public record class ViewInboxJob
{
    public required ViewInboxJobKind Kind { get; init; }

    public required string Id { get; init; }

    // The archive of an export, or the uploaded file of an import, for display and the download.
    public string? FileName { get; init; }
}

public record class ViewInboxItem
{
    public required string Id { get; init; }

    public required ViewInboxItemKind Kind { get; init; }

    // The stable code of what the item is about (ViewExport, ViewImport); clients may branch on it.
    public required string Topic { get; init; }

    public ViewInboxItemLevel Level { get; init; }

    public required string Title { get; init; }

    public string? Text { get; init; }

    // The lowest role of the project that sees the item and may accept or dismiss it.
    public required AccountRole MinimalRole { get; init; }

    public required ViewInboxItemState State { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // Who caused the item: the actor that started the job, or System.
    public required AuditActor CreatedBy { get; init; }

    // The item is removed after this time.
    public required DateTimeOffset ExpiresAt { get; init; }

    // Who accepted or dismissed it, and when; null while open.
    public AuditStamp? Closed { get; init; }

    // Of an Action: the code of what the acceptance runs.
    public string? Action { get; init; }

    public ViewInboxJob? Job { get; init; }
}

public record class ViewInboxResponse
{
    public required IReadOnlyList<ViewInboxItem> Items { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }

    // Every open item the caller sees, whatever the page and the filter.
    public int OpenCount { get; init; }
}
