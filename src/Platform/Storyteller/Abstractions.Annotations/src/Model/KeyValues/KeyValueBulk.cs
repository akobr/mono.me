using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// Keys of a bulk operation (restore, purge, protect): an explicit list, or every key under a prefix.
public record class KeyValueBulkRequest
{
    public IReadOnlyList<string>? Keys { get; init; }

    public string? KeyPrefix { get; init; }

    // Protect only: the target level.
    public KeyValueProtection? Protection { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueBulkOutcome
{
    Done = 0,

    // Already in the requested state.
    Unchanged,

    // Never existed, expired, purged, or not in the state the operation needs.
    NotFound,

    // A secret, and the caller has no secret rights.
    Forbidden,

    // The target level is not allowed, or cannot be reached on the server.
    NotAllowed,

    Failed,
}

public record class KeyValueBulkItemResult
{
    public required string Key { get; init; }

    public required KeyValueBulkOutcome Outcome { get; init; }

    public ulong? Version { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}

public record class KeyValueBulkResult
{
    public required IReadOnlyList<KeyValueBulkItemResult> Items { get; init; }
}
