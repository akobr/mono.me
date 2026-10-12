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

// The Standard level: one RSA software key per project in a Standard vault, wrapping data keys with RSA-OAEP-256.
// The key name is derived from the project, so no state has to be stored to find it again.
public sealed class KeyVaultStandardKeyProvider : IKeyEncryptionProvider
{
    public const string WrapAlgorithmName = "RSA-OAEP-256";

    private readonly KeyClient _client;
    private readonly KeyVaultKeyOptions _keyOptions;
    private readonly bool _emulator;
    private readonly SemaphoreSlim _throttler;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _ensuredKeys = new(StringComparer.Ordinal);
    private readonly ILogger<KeyVaultStandardKeyProvider> _logger;

    public KeyVaultStandardKeyProvider(
        KeyClient client,
        IOptions<KeyValueOptions> options,
        ILogger<KeyVaultStandardKeyProvider>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _keyOptions = options.Value.Protection.Standard;
        _emulator = options.Value.Protection.Emulator;
        _throttler = new SemaphoreSlim(Math.Max(1, options.Value.Protection.MaxConcurrentKeyOperations));
        _logger = logger ?? NullLogger<KeyVaultStandardKeyProvider>.Instance;
    }

    public KeyValueProtection Protection => KeyValueProtection.Standard;

    public Uri VaultUri => _client.VaultUri;

    // {prefix}{32 hex characters of SHA-256("{organization}/{project}")}: valid for Key Vault, and stable per project.
    public static string GetKeyName(string keyNamePrefix, KeyValueScope scope)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope.Organization}/{scope.Project}"));
        return $"{keyNamePrefix}{Convert.ToHexStringLower(hash)[..32]}";
    }

    public Task<string> EnsureKeyAsync(KeyValueScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var name = GetKeyName(_keyOptions.KeyNamePrefix, scope);
        var lazy = _ensuredKeys.GetOrAdd(name, _ => new Lazy<Task<string>>(() => CreateIfMissingAsync(name, cancellationToken)));

        return Awaited(lazy, name);

        async Task<string> Awaited(Lazy<Task<string>> pending, string keyName)
        {
            try
            {
                return await pending.Value;
            }
            catch
            {
                // a failed creation is retried by the next caller
                _ensuredKeys.TryRemove(keyName, out _);
                throw;
            }
        }
    }

    public async Task<WrappedDataKey> WrapAsync(KeyValueScope scope, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureKeyAsync(scope, cancellationToken);
        var name = GetKeyName(_keyOptions.KeyNamePrefix, scope);

        // without a version the latest one is used, and the result carries the versioned key id
        var cryptography = _client.GetCryptographyClient(name);
        var result = await RunAsync(() => cryptography.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey.ToArray(), cancellationToken), cancellationToken);

        return new WrappedDataKey(result.KeyId, WrapAlgorithmName, result.EncryptedKey);
    }

    public async Task<byte[]> UnwrapAsync(KeyValueScope scope, EncryptedValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(value);

        if (!string.Equals(value.Algorithm, WrapAlgorithmName, StringComparison.Ordinal))
        {
            throw new KeyValueIntegrityException($"The data key was wrapped with '{value.Algorithm}', which the Standard level does not use.");
        }

        var identifier = new KeyVaultKeyIdentifier(new Uri(value.KeyId));

        if (!string.Equals(identifier.VaultUri.Host, _client.VaultUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyValueIntegrityException($"The data key was wrapped by a key of vault '{identifier.VaultUri.Host}', not of the configured vault.");
        }

        var cryptography = _client.GetCryptographyClient(identifier.Name, identifier.Version);
        var result = await RunAsync(
            () => cryptography.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, Base64Url.DecodeFromChars(value.WrappedKey), cancellationToken),
            cancellationToken);

        return result.Key;
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

    private async Task<string> CreateIfMissingAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(() => _client.GetKeyAsync(name, cancellationToken: cancellationToken), cancellationToken);
            return GetVersionlessId(name);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // created below
        }

        var options = new CreateRsaKeyOptions(name, hardwareProtected: false)
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

        await RunAsync(() => _client.CreateRsaKeyAsync(options, cancellationToken), cancellationToken);
        _logger.LogInformation("Created the Standard key-encryption key {KeyName} in {Vault}", name, _client.VaultUri);
        await TrySetRotationPolicyAsync(name, cancellationToken);
        return GetVersionlessId(name);
    }

    private async Task TrySetRotationPolicyAsync(string name, CancellationToken cancellationToken)
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
            await RunAsync(() => _client.UpdateKeyRotationPolicyAsync(name, policy, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is RequestFailedException or StorytellerException)
        {
            // rotation is a Phase 1b feature; a vault that refuses the policy still wraps and unwraps
            _logger.LogWarning(exception, "Could not set the rotation policy of the key-encryption key {KeyName}", name);
        }
    }

    private string GetVersionlessId(string name)
    {
        return new Uri(_client.VaultUri, $"keys/{name}").ToString();
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
