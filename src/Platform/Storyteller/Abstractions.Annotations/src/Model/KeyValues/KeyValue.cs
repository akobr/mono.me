using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// One key/value pair at its current (or one requested) version.
public record class KeyValue
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public bool IsSecret => Protection.IsSecret();

    // A public value always; a secret only when revealed; Hyok: the client-encrypted document.
    public string? Value { get; init; }

    public string? ContentType { get; init; }

    public bool IsReference { get; init; }

    // Hyok: the recipient ids the value was encrypted for.
    public IReadOnlyList<string>? Recipients { get; init; }

    public required ulong Version { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required string CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }

    public string? DeletedBy { get; init; }

    // A deleted key: when it is removed for good.
    public DateTimeOffset? ExpiresAt { get; init; }
}
