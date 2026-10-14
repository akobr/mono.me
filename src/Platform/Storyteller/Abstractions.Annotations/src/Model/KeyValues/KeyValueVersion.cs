using System;

namespace _42.Platform.Storyteller;

// One entry of the version list of a key.
public record class KeyValueVersion
{
    public required ulong Version { get; init; }

    public required KeyValueProtection Protection { get; init; }

    // The write that produced this version.
    public required AuditStamp Modified { get; init; }

    // When the version is removed by TTL; null for the current version of an active key.
    public DateTimeOffset? ExpirationTime { get; init; }

    public bool IsCurrent { get; init; }
}
