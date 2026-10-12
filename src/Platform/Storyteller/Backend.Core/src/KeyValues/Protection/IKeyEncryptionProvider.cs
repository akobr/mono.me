namespace _42.Platform.Storyteller.KeyValues.Protection;

public sealed record WrappedDataKey(string KeyId, string Algorithm, byte[] Value);

// Wraps and unwraps data keys with the key-encryption key of one protection level.
public interface IKeyEncryptionProvider
{
    KeyValueProtection Protection { get; }

    // Creates the project's key when missing (Standard, Hsm) or checks the customer's key (Byok). Returns the key and its current version.
    Task<ProtectionKeyInfo> EnsureKeyAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default);

    Task<WrappedDataKey> WrapAsync(KeyValueScope scope, KeyValuePolicy policy, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default);

    Task<byte[]> UnwrapAsync(KeyValueScope scope, KeyValuePolicy policy, EncryptedValue value, CancellationToken cancellationToken = default);

    // The current version of the key, as the daily check and a re-wrap need it.
    Task<ProtectionKeyInfo> GetKeyInfoAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default);

    // Creates a new key version now (Standard, Hsm). Byok throws: the customer rotates.
    Task<ProtectionKeyInfo> RotateAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default);
}

// The HSM provider reports which backend an organization uses: the shared Premium vault or its own Managed HSM.
public interface IHsmBackendProvider
{
    HsmBackend GetBackend(KeyValueScope scope);
}

// The checks of the customer's key before BYOK is enabled.
public interface ICustomerKeyProvider : IKeyEncryptionProvider
{
    ByokSetupInfo GetSetupInfo();

    Task<ByokVerification> VerifyAsync(ByokSettings settings, CancellationToken cancellationToken = default);
}

public sealed record ByokVerification(bool IsValid, string? CurrentKeyVersion, string? Error);
