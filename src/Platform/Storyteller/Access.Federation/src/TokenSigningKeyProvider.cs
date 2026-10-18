using System.Security.Cryptography;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Utils.Async;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// The one key Storyteller tokens are signed with, shared by every instance through ITokenSigningKeyStore.
// Issuing may create it (the first exchange of a deployment); validating never does, so a forged token cannot
// make an instance write a key, and an unknown kid reads the store at most once per 30 seconds per instance.
public class TokenSigningKeyProvider
{
    public const string Algorithm = SecurityAlgorithms.RsaSha256;

    public static readonly TimeSpan IssuingCacheDuration = TimeSpan.FromMinutes(10);

    // How often a validating instance reads the store for a missing key or an unknown kid.
    public static readonly TimeSpan ValidationReloadInterval = TimeSpan.FromSeconds(30);

    private const int KeySizeInBits = 2048;

    private readonly ITokenSigningKeyStore _store;
    private readonly IOptions<FederationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenSigningKeyProvider> _logger;
    private readonly CachedAsync<IssuingState> _issuingKey;
    private readonly SemaphoreSlim _validationGate = new(1, 1);

    private volatile ValidationState? _validation;
    private volatile ValidationFailure? _failure;

    public TokenSigningKeyProvider(
        ITokenSigningKeyStore store,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<TokenSigningKeyProvider> logger)
        : this(store, options, timeProvider, logger, IssuingCacheDuration)
    {
    }

    // Tests shorten the issuing cache, which runs on the wall clock of CachedAsync.
    internal TokenSigningKeyProvider(
        ITokenSigningKeyStore store,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<TokenSigningKeyProvider> logger,
        TimeSpan issuingCacheDuration)
    {
        _store = store;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _issuingKey = new CachedAsync<IssuingState>(
            LoadOrCreateIssuingKeyAsync,
            issuingCacheDuration,
            exception => _logger.LogWarning(exception, "The signing key of Storyteller tokens could not be reloaded; the last answer of the store stays in use"));
    }

    // The key to sign with. Creates it when none is stored and SigningKeyAutoBootstrap allows it;
    // TokenSigningKeyUnavailableException when it doesn't, or when the stored key cannot be used.
    public async Task<SigningCredentials> GetSigningCredentialsAsync()
    {
        var state = await _issuingKey.GetValueAsync();

        if (state.Key is { } key)
        {
            return new SigningCredentials(key.PrivateKey, Algorithm);
        }

        // The store answered that there is no usable key, which no earlier key outlives (the emergency rotation).
        // Not cached: the next exchange reads the store again, so a key an operator stores is used at once.
        _issuingKey.Invalidate();
        throw new TokenSigningKeyUnavailableException(state.UnavailableReason!);
    }

    // The public key a Storyteller token with this kid is checked with, or null when it is not the stored key.
    // Never creates a key. BearerKeyRetrievalException when the store cannot be read and no key is loaded.
    public async Task<SecurityKey?> GetValidationKeyAsync(string kid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(kid))
        {
            return null;
        }

        if (TryAnswerFromLoaded(kid, out var loadedKey))
        {
            return loadedKey;
        }

        await _validationGate.WaitAsync(cancellationToken);

        try
        {
            // Another request may have reloaded while this one waited.
            if (TryAnswerFromLoaded(kid, out loadedKey))
            {
                return loadedKey;
            }

            var current = _validation;
            var now = _timeProvider.GetUtcNow();

            // Nothing loaded and the store just failed: answer 503 without another read.
            if (current is null && _failure is { } failure && now - failure.At < ValidationReloadInterval)
            {
                throw new BearerKeyRetrievalException("The signing key of Storyteller tokens could not be read.", failure.Exception);
            }

            TokenSigningKey? stored;

            try
            {
                stored = await _store.GetAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                if (current is null)
                {
                    _failure = new ValidationFailure(exception, now);
                    throw new BearerKeyRetrievalException("The signing key of Storyteller tokens could not be read.", exception);
                }

                // Keep answering from the loaded key; read again after the reload interval.
                _validation = current with { CheckedAt = now };
                _logger.LogWarning(exception, "The signing key of Storyteller tokens could not be reloaded; the loaded one stays in use");
                return current.Key?.Kid == kid ? current.Key.PublicKey : null;
            }

            var key = stored is null
                ? null
                : current?.Key is { } same && same.Kid == stored.Kid ? same : Load(stored);
            _validation = new ValidationState(key, now, now);
            return key?.Kid == kid ? key.PublicKey : null;
        }
        finally
        {
            _validationGate.Release();
        }
    }

    // RFC 7638: the SHA-256 thumbprint of the public key, base64url encoded.
    internal static string ComputeKid(RSA rsa)
    {
        var publicKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false));
        return Base64UrlEncoder.Encode(publicKey.ComputeJwkThumbprint());
    }

    private static LoadedSigningKey? Load(TokenSigningKey stored)
    {
        if (!string.Equals(stored.Algorithm, Algorithm, StringComparison.Ordinal))
        {
            return null;
        }

        var rsa = RSA.Create();

        try
        {
            rsa.ImportPkcs8PrivateKey(stored.PrivateKeyPkcs8, out _);
        }
        catch (CryptographicException)
        {
            // A corrupt key is unusable, like one of another algorithm.
            rsa.Dispose();
            return null;
        }

        return new LoadedSigningKey(
            stored.Kid,
            new RsaSecurityKey(rsa) { KeyId = stored.Kid },
            new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false)) { KeyId = stored.Kid });
    }

    // Answers without I/O: the loaded key while fresh, and between reloads whatever was loaded last.
    private bool TryAnswerFromLoaded(string kid, out SecurityKey? key)
    {
        key = null;
        var state = _validation;

        if (state is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();

        // Re-read now and then, so deleting the stored key (the emergency rotation) reaches instances that loaded it.
        if (state.Key is { } loaded && loaded.Kid == kid && now - state.LoadedAt < IssuingCacheDuration)
        {
            key = loaded.PublicKey;
            return true;
        }

        if (now - state.CheckedAt < ValidationReloadInterval)
        {
            key = state.Key?.Kid == kid ? state.Key.PublicKey : null;
            return true;
        }

        return false;
    }

    // Throws only when the store cannot be read or written, so CachedAsync keeps the last answer through a store outage.
    // No stored key without bootstrap, and a stored key of another algorithm, are answers of the store: a state without a key.
    private async Task<IssuingState> LoadOrCreateIssuingKeyAsync()
    {
        TokenSigningKey? stored;

        try
        {
            stored = await _store.GetAsync();

            if (stored is null && _options.Value.SigningKeyAutoBootstrap)
            {
                stored = await CreateAsync();
            }
        }
        catch (Exception exception) when (exception is not TokenSigningKeyUnavailableException)
        {
            throw new TokenSigningKeyUnavailableException("The signing key of Storyteller tokens could not be read or created.", exception);
        }

        var key = stored is null ? null : Load(stored);

        // This instance validates as it signs at once, without waiting for the reload interval: its own tokens are
        // accepted, and the tokens of a deleted or unusable key are refused.
        var now = _timeProvider.GetUtcNow();
        _validation = new ValidationState(key, now, now);

        if (stored is null)
        {
            return new IssuingState(null, "No signing key of Storyteller tokens is stored and MachineAuth:Federation:SigningKeyAutoBootstrap is off.");
        }

        return key is null
            ? new IssuingState(null, $"The stored signing key of Storyteller tokens uses {stored.Algorithm}, not {Algorithm}.")
            : new IssuingState(key, null);
    }

    private async Task<TokenSigningKey> CreateAsync()
    {
        using var rsa = RSA.Create(KeySizeInBits);
        var candidate = new TokenSigningKey(ComputeKid(rsa), Algorithm, rsa.ExportPkcs8PrivateKey(), _timeProvider.GetUtcNow());

        if (await _store.TryCreateAsync(candidate))
        {
            _logger.LogInformation("Created the signing key {Kid} of Storyteller tokens", candidate.Kid);
            return candidate;
        }

        // Another instance stored its key first; every instance signs with that one.
        return await _store.GetAsync()
            ?? throw new TokenSigningKeyUnavailableException("The signing key of Storyteller tokens was created concurrently but cannot be read.");
    }

    private sealed record LoadedSigningKey(string Kid, RsaSecurityKey PrivateKey, RsaSecurityKey PublicKey);

    // Key: the key to sign with, or null when the store has no usable one (UnavailableReason says why).
    private sealed record IssuingState(LoadedSigningKey? Key, string? UnavailableReason);

    // Key: the stored key at LoadedAt (null: none stored). CheckedAt: the last read of the store, for the reload gate.
    private sealed record ValidationState(LoadedSigningKey? Key, DateTimeOffset LoadedAt, DateTimeOffset CheckedAt);

    private sealed record ValidationFailure(Exception Exception, DateTimeOffset At);
}
