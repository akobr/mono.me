using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

// The Standard and HSM levels: one RSA key per project in a vault of the platform, wrapping data keys with
// RSA-OAEP-256. Standard keys are software keys in a Standard vault; HSM keys are RSA-HSM keys in a Premium vault,
// or in the organization's own Managed HSM. The key name is derived from the project, so no state is needed to find it.
public sealed class KeyVaultKeyProvider : IKeyEncryptionProvider, IHsmBackendProvider
{
    public const string WrapAlgorithmName = "RSA-OAEP-256";

    private readonly KeyClient? _defaultClient;
    private readonly IReadOnlyDictionary<string, KeyClient> _organizationClients;
    private readonly KeyVaultKeyOptions _keyOptions;
    private readonly bool _emulator;
    private readonly SemaphoreSlim _throttler;
    private readonly ConcurrentDictionary<string, Lazy<Task<ProtectionKeyInfo>>> _ensuredKeys = new(StringComparer.Ordinal);
    private readonly ILogger<KeyVaultKeyProvider> _logger;

    public KeyVaultKeyProvider(
        KeyValueProtection protection,
        KeyClient? defaultClient,
        IReadOnlyDictionary<string, KeyClient> organizationClients,
        KeyVaultKeyOptions keyOptions,
        KeyValueProtectionOptions protectionOptions,
        ILogger<KeyVaultKeyProvider>? logger = null)
    {
        if (protection is not (KeyValueProtection.Standard or KeyValueProtection.Hsm))
        {
            throw new ArgumentOutOfRangeException(nameof(protection), protection, "The provider backs the Standard and Hsm levels.");
        }

        if (defaultClient is null && organizationClients.Count == 0)
        {
            throw new ArgumentException("At least one vault is needed.", nameof(defaultClient));
        }

        Protection = protection;
        _defaultClient = defaultClient;
        _organizationClients = organizationClients;
        _keyOptions = keyOptions ?? throw new ArgumentNullException(nameof(keyOptions));
        _emulator = protectionOptions.Emulator;
        _throttler = new SemaphoreSlim(Math.Max(1, protectionOptions.MaxConcurrentKeyOperations));
        _logger = logger ?? NullLogger<KeyVaultKeyProvider>.Instance;
    }

    public KeyValueProtection Protection { get; }

    // Lowkey Vault has no HSM-protected keys; in emulator mode the HSM level uses software keys too.
    public bool HardwareProtected => Protection == KeyValueProtection.Hsm && !_emulator;

    public static KeyVaultKeyProvider CreateStandard(KeyClient client, IOptions<KeyValueOptions> options, ILogger<KeyVaultKeyProvider>? logger = null)
    {
        return new KeyVaultKeyProvider(
            KeyValueProtection.Standard,
            client,
            new Dictionary<string, KeyClient>(StringComparer.OrdinalIgnoreCase),
            options.Value.Protection.Standard,
            options.Value.Protection,
            logger);
    }

    public static KeyVaultKeyProvider CreateHsm(KeyClient? premiumClient, IReadOnlyDictionary<string, KeyClient> managedHsms, IOptions<KeyValueOptions> options, ILogger<KeyVaultKeyProvider>? logger = null)
    {
        return new KeyVaultKeyProvider(
            KeyValueProtection.Hsm,
            premiumClient,
            managedHsms,
            options.Value.Protection.Hsm,
            options.Value.Protection,
            logger);
    }

    // {prefix}{32 hex characters of SHA-256("{organization}/{project}")}: valid for Key Vault, and stable per project.
    public static string GetKeyName(string keyNamePrefix, KeyValueScope scope)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope.Organization}/{scope.Project}"));
        return $"{keyNamePrefix}{Convert.ToHexStringLower(hash)[..32]}";
    }

    public HsmBackend GetBackend(KeyValueScope scope)
    {
        return _organizationClients.ContainsKey(scope.Organization) ? HsmBackend.ManagedHsm : HsmBackend.KeyVaultPremium;
    }

    public Task<ProtectionKeyInfo> EnsureKeyAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var client = GetClient(scope);
        var name = GetKeyName(_keyOptions.KeyNamePrefix, scope);
        var cacheKey = $"{client.VaultUri}|{name}";
        var lazy = _ensuredKeys.GetOrAdd(cacheKey, _ => new Lazy<Task<ProtectionKeyInfo>>(() => CreateIfMissingAsync(client, name, cancellationToken)));

        return Awaited(lazy, cacheKey);

        async Task<ProtectionKeyInfo> Awaited(Lazy<Task<ProtectionKeyInfo>> pending, string key)
        {
            try
            {
                return await pending.Value;
            }
            catch
            {
                // a failed creation is retried by the next caller
                _ensuredKeys.TryRemove(key, out _);
                throw;
            }
        }
    }

    public async Task<WrappedDataKey> WrapAsync(KeyValueScope scope, KeyValuePolicy policy, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureKeyAsync(scope, policy, cancellationToken);
        var client = GetClient(scope);
        var name = GetKeyName(_keyOptions.KeyNamePrefix, scope);

        // without a version the latest one is used, and the result carries the versioned key id
        var cryptography = client.GetCryptographyClient(name);
        var result = await RunAsync(() => cryptography.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey.ToArray(), cancellationToken), cancellationToken);

        return new WrappedDataKey(result.KeyId, WrapAlgorithmName, result.EncryptedKey);
    }

    public async Task<byte[]> UnwrapAsync(KeyValueScope scope, KeyValuePolicy policy, EncryptedValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(value);

        if (!string.Equals(value.Algorithm, WrapAlgorithmName, StringComparison.Ordinal))
        {
            throw new KeyValueIntegrityException($"The data key was wrapped with '{value.Algorithm}', which the {Protection} level does not use.");
        }

        var client = GetClient(scope);
        var identifier = new KeyVaultKeyIdentifier(new Uri(value.KeyId));

        if (!string.Equals(identifier.VaultUri.Host, client.VaultUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyValueIntegrityException($"The data key was wrapped by a key of vault '{identifier.VaultUri.Host}', not of the configured vault.");
        }

        var cryptography = client.GetCryptographyClient(identifier.Name, identifier.Version);
        var result = await RunAsync(
            () => cryptography.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, Base64Url.DecodeFromChars(value.WrappedKey), cancellationToken),
            cancellationToken);

        return result.Key;
    }

    public async Task<ProtectionKeyInfo> GetKeyInfoAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var client = GetClient(scope);
        var name = GetKeyName(_keyOptions.KeyNamePrefix, scope);
        var key = await RunAsync(() => client.GetKeyAsync(name, cancellationToken: cancellationToken), cancellationToken);
        return ToInfo(client, key.Value);
    }

    public async Task<ProtectionKeyInfo> RotateAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureKeyAsync(scope, policy, cancellationToken);
        var client = GetClient(scope);
        var name = GetKeyName(_keyOptions.KeyNamePrefix, scope);
        var rotated = await RunAsync(() => client.RotateKeyAsync(name, cancellationToken), cancellationToken);
        _logger.LogInformation("Rotated the {Protection} key-encryption key {KeyName} to version {Version}", Protection, name, rotated.Value.Properties.Version);
        return ToInfo(client, rotated.Value);
    }

    private static TimeSpan? GetRetryAfter(RequestFailedException exception)
    {
        var response = exception.GetRawResponse();

        if (response is not null
            && response.Headers.TryGetValue("Retry-After", out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private KeyClient GetClient(KeyValueScope scope)
    {
        if (_organizationClients.TryGetValue(scope.Organization, out var dedicated))
        {
            return dedicated;
        }

        return _defaultClient ?? throw KeyValueErrors.ProtectionNotConfigured(Protection);
    }

    private ProtectionKeyInfo ToInfo(KeyClient client, KeyVaultKey key)
    {
        var created = key.Properties.CreatedOn ?? DateTimeOffset.UtcNow;

        return new ProtectionKeyInfo
        {
            KeyId = new Uri(client.VaultUri, $"keys/{key.Name}").ToString(),
            CurrentKeyVersion = key.Properties.Version,
            NextRotationAt = created + _keyOptions.RotationInterval,
            CheckedAt = DateTimeOffset.UtcNow,
        };
    }

    private async Task<ProtectionKeyInfo> CreateIfMissingAsync(KeyClient client, string name, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await RunAsync(() => client.GetKeyAsync(name, cancellationToken: cancellationToken), cancellationToken);
            return ToInfo(client, existing.Value);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // created below
        }

        var options = new CreateRsaKeyOptions(name, HardwareProtected)
        {
            KeySize = _keyOptions.KeySize,
            KeyOperations = { KeyOperation.WrapKey, KeyOperation.UnwrapKey },
        };

        if (_emulator)
        {
            // Lowkey Vault checks the decrypt operation on unwrap; Key Vault itself needs unwrapKey only
            options.KeyOperations.Add(KeyOperation.Encrypt);
            options.KeyOperations.Add(KeyOperation.Decrypt);
        }

        var created = await RunAsync(() => client.CreateRsaKeyAsync(options, cancellationToken), cancellationToken);
        _logger.LogInformation("Created the {Protection} key-encryption key {KeyName} in {Vault}", Protection, name, client.VaultUri);
        await TrySetRotationPolicyAsync(client, name, cancellationToken);
        return ToInfo(client, created.Value);
    }

    private async Task TrySetRotationPolicyAsync(KeyClient client, string name, CancellationToken cancellationToken)
    {
        var days = Math.Max(1, (int)_keyOptions.RotationInterval.TotalDays);
        var policy = new KeyRotationPolicy
        {
            LifetimeActions =
            {
                new KeyRotationLifetimeAction(KeyRotationPolicyAction.Rotate) { TimeAfterCreate = $"P{days}D" },
            },
        };

        try
        {
            await RunAsync(() => client.UpdateKeyRotationPolicyAsync(name, policy, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is RequestFailedException or StorytellerException)
        {
            // the maintenance run rotates on its own schedule when the vault refuses the policy
            _logger.LogWarning(exception, "Could not set the rotation policy of the key-encryption key {KeyName}", name);
        }
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _throttler.WaitAsync(cancellationToken);

        try
        {
            return await operation();
        }
        catch (RequestFailedException exception) when (exception.Status == 429 || exception.Status >= 500)
        {
            throw KeyValueErrors.KeyEncryptionUnavailable(Protection, GetRetryAfter(exception), exception);
        }
        finally
        {
            _throttler.Release();
        }
    }
}
