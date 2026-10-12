namespace _42.Platform.Storyteller.KeyValues;

public sealed record KeyValueListRequest
{
    public string? KeyPrefix { get; init; }

    public IReadOnlyCollection<KeyValueProtection>? Protections { get; init; }

    public KeyValueState State { get; init; } = KeyValueState.Active;

    // Values of public pairs; secrets are never listed.
    public bool IncludeValues { get; init; }

    public string? ContinuationToken { get; init; }
}

public sealed record KeyValueReadOptions
{
    public bool Reveal { get; init; }

    public bool IncludeDeleted { get; init; }
}

public sealed record KeyValueDeleteOptions
{
    public bool Permanent { get; init; }

    public ulong? ExpectedVersion { get; init; }
}

public sealed record KeyValueExportRequest
{
    public string? KeyPrefix { get; init; }

    public IReadOnlyCollection<KeyValueProtection>? Protections { get; init; }

    public bool IncludeSecrets { get; init; }
}

public sealed record KeyValueImportOptions
{
    public KeyValueImportMode Mode { get; init; } = KeyValueImportMode.Merge;

    public bool DryRun { get; init; }

    // Replace: only the keys under this prefix are replaced.
    public string? KeyPrefix { get; init; }
}

// The current value of a key, for the binding sources.
public sealed record KeyValueLookup(string Key, KeyValueProtection Protection, string Value, string? ContentType);
