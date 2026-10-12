using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// A listing row. The value is present for public pairs only, and only when the listing asked for values.
public record class KeyValueSummary
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public string? Value { get; init; }

    public string? ContentType { get; init; }

    public bool IsReference { get; init; }

    public required ulong Version { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

public record class KeyValuesResponse
{
    public required IReadOnlyList<KeyValueSummary> Items { get; init; }

    public string? ContinuationToken { get; init; }

    public int Count { get; init; }
}
