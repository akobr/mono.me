#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues.Protection;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

/// <summary>
/// The customer's key as a fake: a key id that contains "good" verifies, anything else does not.
/// </summary>
internal sealed class FakeCustomerKeyProvider : ICustomerKeyProvider
{
    private readonly FakeKeyEncryptionProvider _inner = new(KeyValueProtection.Byok);

    public KeyValueProtection Protection => KeyValueProtection.Byok;

    public bool IsRevoked
    {
        get => _inner.IsRevoked;
        set => _inner.IsRevoked = value;
    }

    public int UnwrapCount => _inner.UnwrapCount;

    public ByokSetupInfo GetSetupInfo() => new()
    {
        ClientId = "fake-client",
        ConsentUrl = "https://login.microsoftonline.com/organizations/adminconsent?client_id=fake-client",
        RequiredRole = "Key Vault Crypto Service Encryption User",
    };

    public Task<ByokVerification> VerifyAsync(ByokSettings settings, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(settings.KeyUri.Contains("good", StringComparison.OrdinalIgnoreCase)
            ? new ByokVerification(true, "v1", null)
            : new ByokVerification(false, null, "The key is disabled."));
    }

    public Task<ProtectionKeyInfo> EnsureKeyAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
        => _inner.EnsureKeyAsync(scope, policy, cancellationToken);

    public Task<WrappedDataKey> WrapAsync(KeyValueScope scope, KeyValuePolicy policy, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
        => _inner.WrapAsync(scope, policy, dataKey, cancellationToken);

    public Task<byte[]> UnwrapAsync(KeyValueScope scope, KeyValuePolicy policy, EncryptedValue value, CancellationToken cancellationToken = default)
        => _inner.UnwrapAsync(scope, policy, value, cancellationToken);

    public Task<ProtectionKeyInfo> GetKeyInfoAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
        => _inner.GetKeyInfoAsync(scope, policy, cancellationToken);

    public Task<ProtectionKeyInfo> RotateAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
        => throw new InvalidInputException("The customer rotates a BYOK key.", ErrorCodes.NotSupportedForProtection);
}
