namespace _42.Platform.Storyteller.Api.ConditionalReads;

// Settings of the conditional reads (section ConditionalReads).
public sealed class ConditionalReadOptions
{
    public const string SectionName = "ConditionalReads";

    public const int MinimumKeyBytes = 32;

    private byte[]? _entityTagKey;

    // Base64 of at least 32 bytes that keys the entity tags over resolved bindings, so every instance computes the same tags.
    // Keep it out of committed settings (an app setting or a Key Vault reference); without it each process draws its own key.
    public string? EntityTagKey { get; set; }

    // How long a dependency manifest answers cheap 304s after the resolve that recorded it (absolute, not sliding).
    public TimeSpan ManifestLifetime { get; set; } = TimeSpan.FromMinutes(5);

    // The size of the manifest cache of a process: one per manifest plus one per recorded input.
    public long ManifestCacheSizeLimit { get; set; } = 100_000;

    public bool IsValid => string.IsNullOrWhiteSpace(EntityTagKey) || TryDecode(EntityTagKey, out _);

    public bool IsManifestCacheValid => ManifestLifetime > TimeSpan.Zero && ManifestCacheSizeLimit > 0;

    // The decoded key, or null when none is set.
    public byte[]? GetEntityTagKey()
    {
        if (_entityTagKey is null && !string.IsNullOrWhiteSpace(EntityTagKey) && TryDecode(EntityTagKey, out var key))
        {
            _entityTagKey = key;
        }

        return _entityTagKey;
    }

    private static bool TryDecode(string text, out byte[] key)
    {
        var buffer = new byte[text.Length];

        if (Convert.TryFromBase64String(text.Trim(), buffer, out var length) && length >= MinimumKeyBytes)
        {
            key = buffer[..length];
            return true;
        }

        key = [];
        return false;
    }
}
