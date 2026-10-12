namespace _42.Platform.Storyteller.KeyValues.Protection;

public sealed record WrappedDataKey(string KeyId, string Algorithm, byte[] Value);

// Wraps and unwraps data keys with the key-encryption key of one protection level.
public interface IKeyEncryptionProvider
{
    KeyValueProtection Protection { get; }

    // Creates the project's key when missing. Returns the versionless key id.
    Task<string> EnsureKeyAsync(KeyValueScope scope, CancellationToken cancellationToken = default);

    Task<WrappedDataKey> WrapAsync(KeyValueScope scope, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapAsync(KeyValueScope scope, EncryptedValue value, CancellationToken cancellationToken = default);
}
