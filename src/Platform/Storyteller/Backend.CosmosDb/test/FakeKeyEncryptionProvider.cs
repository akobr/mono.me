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
/// Stands in for <c>KeyVaultStandardKeyProvider</c>: wraps data keys with an in-memory AES key, counts the calls,
/// and can play a vault that is throttling.
/// </summary>
internal sealed class FakeKeyEncryptionProvider : IKeyEncryptionProvider
{
    public const string KeyId = "https://fake.vault.local/keys/kek-test/1";

    private readonly byte[] _kek = RandomNumberGenerator.GetBytes(32);
    private int _wrapCount;
    private int _unwrapCount;

    public KeyValueProtection Protection => KeyValueProtection.Standard;

    public int WrapCount => _wrapCount;

    public int UnwrapCount => _unwrapCount;

    public bool IsUnavailable { get; set; }

    public Task<string> EnsureKeyAsync(KeyValueScope scope, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(KeyId);
    }

    public Task<WrappedDataKey> WrapAsync(KeyValueScope scope, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        Interlocked.Increment(ref _wrapCount);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var wrapped = new byte[12 + dataKey.Length + 16];
        nonce.CopyTo(wrapped, 0);

        using var aes = new AesGcm(_kek, 16);
        aes.Encrypt(nonce, dataKey.Span, wrapped.AsSpan(12, dataKey.Length), wrapped.AsSpan(12 + dataKey.Length, 16));

        return Task.FromResult(new WrappedDataKey(KeyId, "RSA-OAEP-256", wrapped));
    }

    public Task<byte[]> UnwrapAsync(KeyValueScope scope, EncryptedValue value, CancellationToken cancellationToken = default)
    {
        ThrowWhenUnavailable();
        Interlocked.Increment(ref _unwrapCount);

        var wrapped = Base64Url.DecodeFromChars(value.WrappedKey);
        var dataKey = new byte[wrapped.Length - 28];

        using var aes = new AesGcm(_kek, 16);
        aes.Decrypt(wrapped.AsSpan(0, 12), wrapped.AsSpan(12, dataKey.Length), wrapped.AsSpan(12 + dataKey.Length, 16), dataKey);

        return Task.FromResult(dataKey);
    }

    private void ThrowWhenUnavailable()
    {
        if (IsUnavailable)
        {
            throw KeyValueErrors.KeyEncryptionUnavailable(Protection, TimeSpan.FromSeconds(5), new InvalidOperationException("throttled"));
        }
    }
}
