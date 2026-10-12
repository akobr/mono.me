using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueImportMode
{
    // Write the listed keys, leave every other key alone.
    Merge = 0,

    // Write the listed keys and soft-delete every other active key (under the prefix, when given).
    Replace,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueImportAction
{
    Create = 0,
    Update,
    Unchanged,
    Delete,
}

public record class KeyValueImportItemResult
{
    public required string Key { get; init; }

    public required KeyValueProtection Protection { get; init; }

    public required KeyValueImportAction Action { get; init; }

    // The version after a write.
    public ulong? Version { get; init; }

    // Set when the item failed.
    public string? ErrorCode { get; init; }

    public string? Message { get; init; }
}

public record class KeyValueImportResult
{
    public required bool IsDryRun { get; init; }

    // False when an item failed.
    public required bool IsComplete { get; init; }

    public required IReadOnlyList<KeyValueImportItemResult> Items { get; init; }
}
