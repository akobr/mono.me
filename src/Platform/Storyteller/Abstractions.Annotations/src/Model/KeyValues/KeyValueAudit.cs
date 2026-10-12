using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueAuditOperation
{
    Reveal = 0,
    Export,
    BindingRead,
    Create,
    Update,
    Delete,
    Restore,
    PermanentDelete,
    Purge,
    Reprotect,
    Import,
    PolicyChange,
    Rotate,
    Rewrap,
    ByokVerify,
    ByokUnavailable,
    RecipientAdded,
    RecipientChanged,
    RecipientRemoved,
}

// One entry of a project's secret audit trail. Never a value.
public record class KeyValueAuditEvent
{
    public required string Id { get; init; }

    public required DateTimeOffset Time { get; init; }

    public string? View { get; init; }

    public string? Key { get; init; }

    public ulong? Version { get; init; }

    public KeyValueProtection? Protection { get; init; }

    public required KeyValueAuditOperation Operation { get; init; }

    public required string Actor { get; init; }

    public required KeyValueChannel Channel { get; init; }

    // Aggregated binding reads.
    public int Count { get; init; } = 1;

    // Also sent to Key Vault as x-ms-client-request-id, to match the vault's own logs.
    public string? CorrelationId { get; init; }

    public string? Detail { get; init; }
}

public record class KeyValueAuditQuery
{
    public string? View { get; init; }

    public string? Key { get; init; }

    public KeyValueAuditOperation? Operation { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public string? ContinuationToken { get; init; }
}

public record class KeyValueAuditResponse
{
    public required IReadOnlyList<KeyValueAuditEvent> Items { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }
}
