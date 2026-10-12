using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

// The BYOK level: the customer's key in the customer's vault and tenant. The platform reaches it as a multi-tenant
// application whose federated credential is the Functions' managed identity, so no client secret exists. A key
// the customer disabled, deleted or unshared makes every BYOK read and write of the project fail with 424.
public sealed class CustomerKeyProvider : ICustomerKeyProvider
{
    public const string WrapAlgorithmName = "RSA-OAEP-256";
    public const int MinimumKeySize = 2048;

    private readonly CustomerKeyOptions _options;
    private readonly bool _emulator;
    private readonly SemaphoreSlim _throttler;
    private readonly ConcurrentDictionary<string, TokenCredential> _credentials = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, TokenCredential>? _credentialFactory;
    private readonly ILogger<CustomerKeyProvider> _logger;

    public CustomerKeyProvider(
        IOptions<KeyValueOptions> options,
        ILogger<CustomerKeyProvider>? logger = null,
        Func<string, TokenCredential>? credentialFactory = null)
    {
        _options = options.Value.Protection.Byok;
        _emulator = options.Value.Protection.Emulator;
        _throttler = new SemaphoreSlim(Math.Max(1, options.Value.Protection.MaxConcurrentKeyOperations));
        _credentialFactory = credentialFactory;
        _logger = logger ?? NullLogger<CustomerKeyProvider>.Instance;
    }

    public KeyValueProtection Protection => KeyValueProtection.Byok;

    public ByokSetupInfo GetSetupInfo()
    {
        var clientId = _options.ClientId ?? (_emulator ? "emulator" : throw KeyValueErrors.ProtectionNotConfigured(Protection));

        return new ByokSetupInfo
        {
            ClientId = clientId,
            ConsentUrl = $"https://login.microsoftonline.com/organizations/adminconsent?client_id={Uri.EscapeDataString(clientId)}",
            RequiredRole = _options.RequiredRole,
        };
    }

    public async Task<ByokVerification> VerifyAsync(ByokSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var (client, identifier) = GetKeyClient(settings);
            var key = (await RunAsync(() => client.GetKeyAsync(identifier.Name, cancellationToken: cancellationToken), cancellationToken)).Value;

            if (key.Properties.Enabled == false)
            {
                return new ByokVerification(false, key.Properties.Version, "The key is disabled.");
            }

            if (key.KeyType != KeyType.Rsa && key.KeyType != KeyType.RsaHsm)
            {
                return new ByokVerification(false, key.Properties.Version, $"The key is {key.KeyType}; an RSA key of at least {MinimumKeySize} bits is needed.");
            }

            var keySize = (key.Key.N?.Length ?? 0) * 8;

            if (keySize < MinimumKeySize)
            {
                return new ByokVerification(false, key.Properties.Version, $"The key has {keySize} bits; at least {MinimumKeySize} are needed.");
            }

            if (!key.KeyOperations.Contains(KeyOperation.WrapKey) || !key.KeyOperations.Contains(KeyOperation.UnwrapKey))
            {
                return new ByokVerification(false, key.Properties.Version, "The key must allow the wrapKey and unwrapKey operations.");
            }

            var cryptography = client.GetCryptographyClient(identifier.Name);
            var sample = RandomNumberGenerator.GetBytes(32);
            var wrapped = await RunAsync(() => cryptography.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, sample, cancellationToken), cancellationToken);

            // unwrap with the version that wrapped, as the stored envelopes are unwrapped later
            var wrappedBy = new KeyVaultKeyIdentifier(new Uri(wrapped.KeyId));
            var versioned = client.GetCryptographyClient(wrappedBy.Name, wrappedBy.Version);
            var unwrapped = await RunAsync(() => versioned.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrapped.EncryptedKey, cancellationToken), cancellationToken);

            if (!unwrapped.Key.AsSpan().SequenceEqual(sample))
            {
                return new ByokVerification(false, key.Properties.Version, "A wrap and unwrap round trip did not return the input.");
            }

            return new ByokVerification(true, key.Properties.Version, null);
        }
        catch (RequestFailedException exception)
        {
            return new ByokVerification(false, null, $"The vault answered {exception.Status}: {exception.ErrorCode ?? exception.Message}");
        }
        catch (StorytellerException exception)
        {
            return new ByokVerification(false, null, exception.Message);
        }
        catch (Exception exception) when (exception is UriFormatException or ArgumentException or AuthenticationFailedException)
        {
            return new ByokVerification(false, null, exception.Message);
        }
    }

    public async Task<ProtectionKeyInfo> EnsureKeyAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        return await GetKeyInfoAsync(scope, policy, cancellationToken);
    }

    public async Task<WrappedDataKey> WrapAsync(KeyValueScope scope, KeyValuePolicy policy, ReadOnlyMemory<byte> dataKey, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings(policy);
        var (client, identifier) = GetKeyClient(settings);
        var cryptography = client.GetCryptographyClient(identifier.Name);
        var result = await RunAsync(() => cryptography.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey.ToArray(), cancellationToken), cancellationToken);
        return new WrappedDataKey(result.KeyId, WrapAlgorithmName, result.EncryptedKey);
    }

    public async Task<byte[]> UnwrapAsync(KeyValueScope scope, KeyValuePolicy policy, EncryptedValue value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var settings = RequireSettings(policy);

        if (!string.Equals(value.Algorithm, WrapAlgorithmName, StringComparison.Ordinal))
        {
            throw new KeyValueIntegrityException($"The data key was wrapped with '{value.Algorithm}', which the Byok level does not use.");
        }

        var (client, configured) = GetKeyClient(settings);
        var wrappedBy = new KeyVaultKeyIdentifier(new Uri(value.KeyId));

        if (!string.Equals(wrappedBy.VaultUri.Host, configured.VaultUri.Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(wrappedBy.Name, configured.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw KeyValueErrors.CustomerKeyUnavailable($"the value was wrapped by '{wrappedBy.Name}' of '{wrappedBy.VaultUri.Host}', which is not the configured customer key");
        }

        var cryptography = client.GetCryptographyClient(wrappedBy.Name, wrappedBy.Version);
        var result = await RunAsync(
            () => cryptography.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, Base64Url.DecodeFromChars(value.WrappedKey), cancellationToken),
            cancellationToken);

        return result.Key;
    }

    public async Task<ProtectionKeyInfo> GetKeyInfoAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        var settings = RequireSettings(policy);
        var (client, identifier) = GetKeyClient(settings);
        var key = (await RunAsync(() => client.GetKeyAsync(identifier.Name, cancellationToken: cancellationToken), cancellationToken)).Value;

        if (key.Properties.Enabled == false)
        {
            throw KeyValueErrors.CustomerKeyUnavailable("the customer disabled the key");
        }

        return new ProtectionKeyInfo
        {
            KeyId = settings.KeyUri,
            CurrentKeyVersion = key.Properties.Version,
            CheckedAt = DateTimeOffset.UtcNow,
        };
    }

    public Task<ProtectionKeyInfo> RotateAsync(KeyValueScope scope, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        throw new InvalidInputException("The customer rotates a BYOK key in their own vault; the platform only picks the new version up.", ErrorCodes.NotSupportedForProtection);
    }

    private static ByokSettings RequireSettings(KeyValuePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Byok ?? throw new ConflictException("The project has no verified customer key (BYOK).", ErrorCodes.ByokNotConfigured);
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

    private (KeyClient Client, KeyVaultKeyIdentifier Identifier) GetKeyClient(ByokSettings settings)
    {
        if (!Uri.TryCreate(settings.KeyUri, UriKind.Absolute, out var keyUri))
        {
            throw new InvalidInputException($"The customer key id '{settings.KeyUri}' is not a URI.", ErrorCodes.CustomerKeyInvalid);
        }

        var identifier = new KeyVaultKeyIdentifier(keyUri);
        var options = new KeyClientOptions();

        if (_emulator)
        {
            options.DisableChallengeResourceVerification = true;
            options.Transport = new HttpClientTransport(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        return (new KeyClient(identifier.VaultUri, GetCredential(settings.TenantId), options), identifier);
    }

    private TokenCredential GetCredential(string tenantId)
    {
        return _credentials.GetOrAdd(tenantId, tenant =>
        {
            if (_credentialFactory is not null)
            {
                return _credentialFactory(tenant);
            }

            if (_emulator)
            {
                return new EmulatorTokenCredential();
            }

            if (!_options.IsConfigured)
            {
                throw KeyValueErrors.ProtectionNotConfigured(Protection);
            }

            // the managed identity proves who we are to our own tenant; the assertion gets us a token for the customer's tenant
            var managedIdentity = string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_options.ManagedIdentityClientId));

            return new ClientAssertionCredential(
                tenant,
                _options.ClientId!,
                async cancellationToken =>
                {
                    var token = await managedIdentity.GetTokenAsync(new TokenRequestContext(["api://AzureADTokenExchange/.default"]), cancellationToken);
                    return token.Token;
                });
        });
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _throttler.WaitAsync(cancellationToken);

        try
        {
            return await operation();
        }
        catch (RequestFailedException exception) when (exception.Status is 401 or 403 or 404)
        {
            _logger.LogWarning(exception, "The customer key answered {Status}", exception.Status);
            throw KeyValueErrors.CustomerKeyUnavailable($"the vault answered {exception.Status} ({exception.ErrorCode ?? "no error code"})", exception);
        }
        catch (RequestFailedException exception) when (exception.Status == 429 || exception.Status >= 500)
        {
            throw KeyValueErrors.KeyEncryptionUnavailable(Protection, GetRetryAfter(exception), exception);
        }
        catch (AuthenticationFailedException exception)
        {
            _logger.LogWarning(exception, "The customer tenant refused the platform identity");
            throw KeyValueErrors.CustomerKeyUnavailable("the customer tenant refused the platform identity (consent or role missing)", exception);
        }
        finally
        {
            _throttler.Release();
        }
    }
}
