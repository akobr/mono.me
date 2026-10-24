namespace _42.Platform.Storyteller.Mcp.Understanding;

// What a binding's key/value pair is, never its value: read with reveal=false. A failed read leaves Exists unknown
// (null) and names its Status and ErrorCode.
public sealed record KeyValueMetadata
{
    public static KeyValueMetadata Missing { get; } = new() { Exists = false };

    public bool? Exists { get; init; }

    public bool? IsDeleted { get; init; }

    public Sdk.KeyValueProtection? Protection { get; init; }

    public bool? IsSecret { get; init; }

    public long? Version { get; init; }

    public int? Status { get; init; }

    public string? ErrorCode { get; init; }

    public static KeyValueMetadata From(Sdk.KeyValue pair)
    {
        return new KeyValueMetadata
        {
            Exists = true,
            IsDeleted = pair.IsDeleted,
            Protection = pair.Protection,
            IsSecret = pair.IsSecret,
            Version = pair.Version,
        };
    }

    public static KeyValueMetadata Failed(int status, string? errorCode)
    {
        return new KeyValueMetadata { Status = status, ErrorCode = errorCode };
    }
}
