using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues.Protection;

// Envelope encryption: a random 256-bit data key and a random 96-bit nonce per version, AES-256-GCM with the
// place of the version as associated data, and the data key wrapped by the level's key-encryption key.
public sealed class EnvelopeValueProtector : IValueProtector, IDisposable
{
    public const string AssociatedDataPrefix = "2s.kv.v1";

    private const int DataKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IReadOnlyDictionary<KeyValueProtection, IKeyEncryptionProvider> _providers;
    private readonly KeyValueProtectionOptions _options;
    private readonly MemoryCache _cache;
    private readonly ILogger<EnvelopeValueProtector> _logger;

    public EnvelopeValueProtector(
        IEnumerable<IKeyEncryptionProvider> providers,
        IOptions<KeyValueOptions> options,
        ILogger<EnvelopeValueProtector>? logger = null)
    {
        _providers = providers.ToDictionary(provider => provider.Protection);
        _options = options.Value.Protection;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = Math.Max(1, _options.CacheSizeLimit) });
        _logger = logger ?? NullLogger<EnvelopeValueProtector>.Instance;
    }

    public static byte[] BuildAssociatedData(KeyValueIdentity identity, KeyValueProtection protection)
    {
        return Encoding.UTF8.GetBytes(
            $"{AssociatedDataPrefix}|{identity.Organization}|{identity.Project}|{identity.View}|{identity.Key}|{identity.Version}|{protection}");
    }

    public async Task<StoredValue> ProtectAsync(
        KeyValueIdentity identity,
        KeyValueProtection protection,
        string value,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(value);

        var plaintext = Encoding.UTF8.GetBytes(value);

        switch (protection)
        {
            case KeyValueProtection.Public:
                return new StoredValue
                {
                    Protection = protection,
                    ContentType = contentType,
                    Value = value,
                    Length = plaintext.Length,
                };

            case KeyValueProtection.Standard:
            case KeyValueProtection.Hsm:
            case KeyValueProtection.Byok:
                return await EncryptAsync(identity, protection, plaintext, contentType, cancellationToken);

            default:
                // client-side encryption (Hyok) is validated and stored by Phase 1b
                throw KeyValueErrors.ProtectionNotConfigured(protection);
        }
    }

    public async Task<string> UnprotectAsync(KeyValueIdentity identity, StoredValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(value);

        switch (value.Protection)
        {
            case KeyValueProtection.Public:
            case KeyValueProtection.Hyok:
                return value.Value ?? string.Empty;

            case KeyValueProtection.Standard:
            case KeyValueProtection.Hsm:
            case KeyValueProtection.Byok:
                return await DecryptAsync(identity, value, cancellationToken);

            default:
                throw new KeyValueIntegrityException($"Unknown protection '{value.Protection}' of '{identity.Key}' version {identity.Version}.");
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private static string GetCacheKey(KeyValueIdentity identity)
    {
        return $"{identity.Organization}/{identity.Project}/{identity.View}/{identity.Key}/{identity.Version}";
    }

    private IKeyEncryptionProvider GetProvider(KeyValueProtection protection)
    {
        return _providers.TryGetValue(protection, out var provider)
            ? provider
            : throw KeyValueErrors.ProtectionNotConfigured(protection);
    }

    private TimeSpan GetCacheDuration(KeyValueProtection protection)
    {
        // the HSM and BYOK levels get their own durations in Phase 1b
        return _options.Standard.CacheDuration;
    }

    private async Task<StoredValue> EncryptAsync(
        KeyValueIdentity identity,
        KeyValueProtection protection,
        byte[] plaintext,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var provider = GetProvider(protection);
        var dataKey = new byte[DataKeySize];

        try
        {
            RandomNumberGenerator.Fill(dataKey);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length + TagSize];

            using (var aes = new AesGcm(dataKey, TagSize))
            {
                aes.Encrypt(
                    nonce,
                    plaintext,
                    ciphertext.AsSpan(0, plaintext.Length),
                    ciphertext.AsSpan(plaintext.Length, TagSize),
                    BuildAssociatedData(identity, protection));
            }

            var wrapped = await provider.WrapAsync(identity.Scope, dataKey, cancellationToken);

            return new StoredValue
            {
                Protection = protection,
                ContentType = contentType,
                Length = plaintext.Length,
                Encrypted = new EncryptedValue
                {
                    KeyId = wrapped.KeyId,
                    Algorithm = wrapped.Algorithm,
                    WrappedKey = Base64Url.EncodeToString(wrapped.Value),
                    Nonce = Base64Url.EncodeToString(nonce),
                    Ciphertext = Base64Url.EncodeToString(ciphertext),
                },
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private async Task<string> DecryptAsync(KeyValueIdentity identity, StoredValue value, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(identity);

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        var encrypted = value.Encrypted
            ?? throw new KeyValueIntegrityException($"The '{value.Protection}' value of '{identity.Key}' version {identity.Version} has no ciphertext.");

        var provider = GetProvider(value.Protection);
        var dataKey = await provider.UnwrapAsync(identity.Scope, encrypted, cancellationToken);
        byte[]? plaintext = null;

        try
        {
            var nonce = Base64Url.DecodeFromChars(encrypted.Nonce);
            var ciphertext = Base64Url.DecodeFromChars(encrypted.Ciphertext);

            if (dataKey.Length != DataKeySize || nonce.Length != NonceSize || ciphertext.Length < TagSize)
            {
                throw new KeyValueIntegrityException($"The envelope of '{identity.Key}' version {identity.Version} is malformed.");
            }

            plaintext = new byte[ciphertext.Length - TagSize];

            try
            {
                using var aes = new AesGcm(dataKey, TagSize);
                aes.Decrypt(
                    nonce,
                    ciphertext.AsSpan(0, plaintext.Length),
                    ciphertext.AsSpan(plaintext.Length, TagSize),
                    plaintext,
                    BuildAssociatedData(identity, value.Protection));
            }
            catch (CryptographicException exception)
            {
                _logger.LogError(exception, "The ciphertext of '{Key}' version {Version} failed its integrity check", identity.Key, identity.Version);
                throw new KeyValueIntegrityException($"The ciphertext of '{identity.Key}' version {identity.Version} failed its integrity check.", exception);
            }

            var text = Encoding.UTF8.GetString(plaintext);
            var duration = GetCacheDuration(value.Protection);

            if (duration > TimeSpan.Zero)
            {
                _cache.Set(cacheKey, text, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = duration,
                    Size = Math.Max(1, plaintext.Length),
                });
            }

            return text;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }
}
