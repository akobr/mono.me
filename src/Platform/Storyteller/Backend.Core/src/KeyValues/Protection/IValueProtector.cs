namespace _42.Platform.Storyteller.KeyValues.Protection;

// Turns a value into its stored form and back, by protection level. Owns the plaintext cache.
public interface IValueProtector
{
    Task<StoredValue> ProtectAsync(KeyValueIdentity identity, KeyValueProtection protection, string value, string? contentType, CancellationToken cancellationToken = default);

    // The plaintext; for Hyok the client-encrypted document.
    Task<string> UnprotectAsync(KeyValueIdentity identity, StoredValue value, CancellationToken cancellationToken = default);
}
