#nullable enable

using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Protection;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

/// <summary>
/// Stands in for the Key Vault providers: wraps data keys with an in-memory AES key per key version, counts the
/// calls, can rotate, and can play a vault that is throttling or a customer key that was revoked.
/// </summary>
internal sealed class FakeKeyEncryptionProvider : IKeyEncryptionProvider, IHsmBackendProvider
{
    private readonly byte[] _kek = RandomNumberGenerator.GetBytes(32);
    private int _version = 1;
    private int _wrapCount;
    private int _unwrapCount;

    public FakeKeyEncryptionProvider(KeyValueProtection protection = KeyValueProtection.Standard)
    {
        Protection = protection;
    }

    public KeyValueProtection Protection { get; }

    public string KeyId => $"https://fake.vault.local/keys/kek-{Protection.ToString().ToLowerInvariant()}";

    public string CurrentVersion => _version.ToString();

    public int WrapCount => _wrapCount;

    public int UnwrapCount => _unwrapCount;

    public bool IsUnavailable { get; set; }

    public bool IsRevoked { get; set; }

    public HsmBackend GetBackend(KeyValueScope scope) => HsmBackend.KeyVaultPremium;

    public Task<ProtectionKeyInfo> EnsureKeyAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        return Task.FromResult(Info());
    }

    public Task<WrappedDataKey> WrapAsync(KeyValueScope scope, KeyValuePolicy policy, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        Interlocked.Increment(ref _wrapCount);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var wrapped = new byte[12 + dataKey.Length + 16];
        nonce.CopyTo(wrapped, 0);

        using var aes = new AesGcm(_kek, 16);
        aes.Encrypt(nonce, dataKey.Span, wrapped.AsSpan(12, dataKey.Length), wrapped.AsSpan(12 + dataKey.Length, 16));

        return Task.FromResult(new WrappedDataKey($"{KeyId}/{_version}", "RSA-OAEP-256", wrapped));
    }

    public Task<byte[]> UnwrapAsync(KeyValueScope scope, KeyValuePolicy policy, EncryptedValue value, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        Interlocked.Increment(ref _unwrapCount);

        var wrapped = Base64Url.DecodeFromChars(value.WrappedKey);
        var dataKey = new byte[wrapped.Length - 28];

        using var aes = new AesGcm(_kek, 16);
        aes.Decrypt(wrapped.AsSpan(0, 12), wrapped.AsSpan(12, dataKey.Length), wrapped.AsSpan(12 + dataKey.Length, 16), dataKey);

        return Task.FromResult(dataKey);
    }

    public Task<ProtectionKeyInfo> GetKeyInfoAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        return Task.FromResult(Info());
    }

    public Task<ProtectionKeyInfo> RotateAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        Interlocked.Increment(ref _version);
        return Task.FromResult(Info());
    }

    private ProtectionKeyInfo Info() => new()
    {
        KeyId = KeyId,
        CurrentKeyVersion = CurrentVersion,
        CheckedAt = DateTimeOffset.UtcNow,
    };

    private void ThrowWhenUnavailable()
    {
        if (IsRevoked)
        {
            throw KeyValueErrors.CustomerKeyUnavailable("the customer disabled the key");
        }

        if (IsUnavailable)
        {
            throw KeyValueErrors.KeyEncryptionUnavailable(Protection, TimeSpan.FromSeconds(5), new InvalidOperationException("throttled"));
        }
    }
}
