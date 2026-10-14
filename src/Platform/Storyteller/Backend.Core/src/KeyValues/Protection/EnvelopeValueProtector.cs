using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues.Protection;

// Envelope encryption: a random 256-bit data key and a random 96-bit nonce per version, AES-256-GCM with the
// place of the version as associated data, and the data key wrapped by the level's key-encryption key.
// Client-encrypted (Hyok) values are validated against the project's recipients and stored as they are.
public sealed class EnvelopeValueProtector : IValueProtector, IDisposable
{
    public const string AssociatedDataPrefix = "2s.kv.v1";

    private const int DataKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IReadOnlyDictionary<KeyValueProtection, IKeyEncryptionProvider> _providers;
    private readonly IKeyValuePolicyStore _policies;
    private readonly KeyValueProtectionOptions _options;
    private readonly IKeyValueUsageRecorder? _usage;
    private readonly MemoryCache _cache;
    private readonly ILogger<EnvelopeValueProtector> _logger;

    public EnvelopeValueProtector(
        IEnumerable<IKeyEncryptionProvider> providers,
        IKeyValuePolicyStore policies,
        IOptions<KeyValueOptions> options,
        IKeyValueUsageRecorder? usage = null,
        ILogger<EnvelopeValueProtector>? logger = null)
    {
        _providers = providers.ToDictionary(provider => provider.Protection);
        _policies = policies;
        _options = options.Value.Protection;
        _usage = usage;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = Math.Max(1, _options.CacheSizeLimit) });
        _logger = logger ?? NullLogger<EnvelopeValueProtector>.Instance;
    }

    public IReadOnlyCollection<KeyValueProtection> ConfiguredProtections => _providers.Keys.ToList();

    public static byte[] BuildAssociatedData(KeyValueIdentity identity, KeyValueProtection protection)
    {
        return Encoding.UTF8.GetBytes(
            $"{AssociatedDataPrefix}|{identity.Organization}|{identity.Project}|{identity.View}|{identity.Key}|{identity.Version}|{protection}");
    }

    public bool IsConfigured(KeyValueProtection protection)
    {
        return protection is KeyValueProtection.Public or KeyValueProtection.Hyok || _providers.ContainsKey(protection);
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

        switch (protection)
        {
            case KeyValueProtection.Public:
                return new StoredValue
                {
                    Protection = protection,
                    ContentType = contentType,
                    Value = value,
                    Length = Encoding.UTF8.GetByteCount(value),
                };

            case KeyValueProtection.Hyok:
                var policy = await _policies.GetAsync(identity.Organization, identity.Project, cancellationToken);
                var recipients = ClientEncryptedValueValidator.Validate(value, identity, policy.Recipients);
                return new StoredValue
                {
                    Protection = protection,
                    ContentType = contentType,
                    Value = value,
                    Recipients = recipients,
                    Length = value.Length,
                };

            case KeyValueProtection.Standard:
            case KeyValueProtection.Hsm:
            case KeyValueProtection.Byok:
                return await EncryptAsync(identity, protection, Encoding.UTF8.GetBytes(value), contentType, cancellationToken);

            default:
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

    // The identity and a fingerprint of the envelope: a plaintext is reused only for the same ciphertext at that identity
    // (a key purged and written again starts at version 1 within the cache window).
    private static string GetCacheKey(KeyValueIdentity identity, EncryptedValue encrypted)
    {
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{encrypted.KeyId}|{encrypted.WrappedKey}|{encrypted.Nonce}|{encrypted.Ciphertext}"));
        return $"{identity.Organization}/{identity.Project}/{identity.View}/{identity.Key}/{identity.Version}/{Convert.ToHexStringLower(fingerprint)[..32]}";
    }

    private IKeyEncryptionProvider GetProvider(KeyValueProtection protection)
    {
        return _providers.TryGetValue(protection, out var provider)
            ? provider
            : throw KeyValueErrors.ProtectionNotConfigured(protection);
    }

    private TimeSpan GetCacheDuration(KeyValueProtection protection)
    {
        return protection switch
        {
            KeyValueProtection.Hsm => _options.Hsm.CacheDuration,
            KeyValueProtection.Byok => _options.Byok.CacheDuration,
            _ => _options.Standard.CacheDuration,
        };
    }

    private async Task<StoredValue> EncryptAsync(
        KeyValueIdentity identity,
        KeyValueProtection protection,
        byte[] plaintext,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var provider = GetProvider(protection);
        var policy = await _policies.GetAsync(identity.Organization, identity.Project, cancellationToken);
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

            var wrapped = await provider.WrapAsync(identity.Scope, policy, dataKey, cancellationToken);
            _usage?.Add(identity.Organization, identity.Project, KeyValueUsageMeters.KmsOf(protection));

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
        var encrypted = value.Encrypted
            ?? throw new KeyValueIntegrityException($"The '{value.Protection}' value of '{identity.Key}' version {identity.Version} has no ciphertext.");
        var cacheKey = GetCacheKey(identity, encrypted);

        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        var provider = GetProvider(value.Protection);
        var policy = await _policies.GetAsync(identity.Organization, identity.Project, cancellationToken);
        var dataKey = await provider.UnwrapAsync(identity.Scope, policy, encrypted, cancellationToken);
        _usage?.Add(identity.Organization, identity.Project, KeyValueUsageMeters.KmsOf(value.Protection));
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
