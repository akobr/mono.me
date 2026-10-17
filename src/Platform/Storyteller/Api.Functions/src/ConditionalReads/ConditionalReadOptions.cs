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

    public bool IsValid => string.IsNullOrWhiteSpace(EntityTagKey) || TryDecode(EntityTagKey, out _);

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
