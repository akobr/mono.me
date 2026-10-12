using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// The import and export format. Clients convert .env and flat JSON to it.
public record class KeyValueDocument
{
    public const string CurrentFormat = "2s.key-values.v1";

    public string Format { get; init; } = CurrentFormat;

    public required IReadOnlyList<KeyValueDocumentItem> Items { get; init; }
}

public record class KeyValueDocumentItem
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    // Omitted for a secret exported without values; such an item leaves an existing secret unchanged.
    public string? Value { get; init; }

    public string? ContentType { get; init; }
}
