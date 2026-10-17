using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// The signing keys of trusted issuers, from an own cache rather than ConfigurationManager, whose background refresh
// would let a retry after an unknown kid see the stale keys. One entry per project, issuer name and issuer settings
// (a changed issuer starts a new entry), with a sliding hour; concurrent requests share one fetch per entry.
public class FederatedIssuerKeyResolver
{
    // A token naming an unknown kid fetches the keys again only when they are older than this.
    public static readonly TimeSpan UnknownKeyRefetchAge = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromHours(1);

    // A failed fetch answers the next requests for as long as the Retry-After the token endpoint sends,
    // so an unreachable issuer does not queue a 10-second fetch per request.
    private static readonly TimeSpan FailureRetention = TimeSpan.FromSeconds(5);

    // A misconfigured issuer (its discovery names another issuer or an unsafe jwks_uri) is checked again after this.
    private static readonly TimeSpan MisconfigurationRetention = TimeSpan.FromMinutes(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<FederationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FederatedIssuerKeyResolver> _logger;
    private readonly Lock _entryLock = new();

    public FederatedIssuerKeyResolver(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<FederatedIssuerKeyResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // The limit of one fetch, its body included: HttpClient.Timeout stops covering a response once its headers are in,
    // so an issuer host that sends the body slowly would otherwise hold the fetch, and the requests waiting for it.
    internal TimeSpan FetchTimeout { get; init; } = FederationMetadataHttpHandler.Timeout;

    // The keys of the trusted issuer. Null when the issuer is misconfigured: logged at Warning and refused like a rule
    // mismatch. BearerKeyRetrievalException when the keys cannot be fetched (a network or HTTP failure).
    public async Task<FederatedIssuerKeys?> GetKeysAsync(
        string organization,
        string project,
        FederatedIssuer issuer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        var entry = GetEntry(organization, project, issuer);

        if (entry.State is { } state && IsCurrent(state))
        {
            return Resolve(state);
        }

        await entry.Gate.WaitAsync(cancellationToken);

        try
        {
            // Another request may have fetched while this one waited.
            if (entry.State is { } fetched && IsCurrent(fetched))
            {
                return Resolve(fetched);
            }

            entry.State = await LoadAsync(organization, project, issuer, cancellationToken);
            return Resolve(entry.State);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    // For a token whose kid none of the stale keys has. Keys older than a minute are fetched again at once; within that
    // minute the issuer may be rotating, so BearerKeyRetrievalException (503, the client retries). Null for a stored JWKS
    // (nothing to fetch, so the token is refused) or an issuer that turned out misconfigured.
    public async Task<FederatedIssuerKeys?> RefreshForUnknownKeyAsync(
        string organization,
        string project,
        FederatedIssuer issuer,
        FederatedIssuerKeys stale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(stale);

        if (stale.IsStatic)
        {
            return null;
        }

        var entry = GetEntry(organization, project, issuer);

        if (entry.State?.Keys is { } newer && newer.FetchedAt > stale.FetchedAt)
        {
            return newer;
        }

        if (_timeProvider.GetUtcNow() - stale.FetchedAt < UnknownKeyRefetchAge)
        {
            throw new BearerKeyRetrievalException($"The token names a key the trusted issuer {issuer.Name} did not publish within the last minute.");
        }

        await entry.Gate.WaitAsync(cancellationToken);

        try
        {
            if (entry.State is { } state && state.CheckedAt > stale.FetchedAt && IsCurrent(state))
            {
                return Resolve(state);
            }

            entry.State = await LoadAsync(organization, project, issuer, cancellationToken);
            return Resolve(entry.State);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    internal static string GetCacheKey(string organization, string project, FederatedIssuer issuer)
    {
        // The settings the keys depend on; an issuer replaced by an administrator gets a new entry at once.
        var settings = string.Join('\n', issuer.Issuer, issuer.KeySource.ToString(), issuer.JwksUri ?? string.Empty, issuer.Jwks ?? string.Empty);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings)), 0, 16);
        return $"fedkeys:{organization}.{project}.{issuer.Name}:{fingerprint}";
    }

    private static FederatedIssuerKeys? Resolve(KeyState state)
    {
        return state.Failure is { } failure
            ? throw new BearerKeyRetrievalException(failure.Message, failure)
            : state.Keys;
    }

    private static IReadOnlyList<SecurityKey>? ParseKeySet(string json)
    {
        JsonWebKeySet set;

        try
        {
            set = new JsonWebKeySet(json);
        }
        catch (ArgumentException)
        {
            return null;
        }

        // Public RSA and EC signature keys only; an `oct` key would be an HMAC secret.
        return set.Keys
            .Where(key => string.IsNullOrEmpty(key.Use) || key.Use == JsonWebKeyUseNames.Sig)
            .Where(key => (key.Kty == JsonWebAlgorithmsKeyTypes.RSA && !string.IsNullOrEmpty(key.N) && !string.IsNullOrEmpty(key.E))
                || (key.Kty == JsonWebAlgorithmsKeyTypes.EllipticCurve && !string.IsNullOrEmpty(key.Crv) && !string.IsNullOrEmpty(key.X) && !string.IsNullOrEmpty(key.Y)))
            .Cast<SecurityKey>()
            .ToList();
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private bool IsCurrent(KeyState state)
    {
        var age = _timeProvider.GetUtcNow() - state.CheckedAt;

        return state switch
        {
            { Failure: not null } => age < FailureRetention,
            { Keys: null } => age < MisconfigurationRetention,
            { Keys.IsStatic: true } => true,
            _ => age < TimeSpan.FromMinutes(_options.Value.MetadataRefreshMinutes),
        };
    }

    private KeyCacheEntry GetEntry(string organization, string project, FederatedIssuer issuer)
    {
        var key = GetCacheKey(organization, project, issuer);

        // Under a lock, so concurrent first requests share one entry and so one fetch.
        lock (_entryLock)
        {
            return _cache.GetOrCreate(key, cacheEntry =>
            {
                cacheEntry.SlidingExpiration = CacheSlidingExpiration;
                cacheEntry.Size = 1;
                return new KeyCacheEntry();
            })!;
        }
    }

    private async Task<KeyState> LoadAsync(string organization, string project, FederatedIssuer issuer, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        try
        {
            var keys = issuer.KeySource switch
            {
                FederatedIssuerKeySource.Jwks => LoadStoredKeys(organization, project, issuer),
                FederatedIssuerKeySource.JwksUri => await FetchKeySetAsync(organization, project, issuer, issuer.JwksUri, cancellationToken),
                _ => await DiscoverKeysAsync(organization, project, issuer, cancellationToken),
            };

            return new KeyState(
                keys is null ? null : new FederatedIssuerKeys(keys, now, issuer.KeySource == FederatedIssuerKeySource.Jwks),
                now,
                null);
        }
        catch (BearerKeyRetrievalException exception)
        {
            _logger.LogWarning(
                exception,
                "The keys of the trusted issuer {IssuerName} of {Organization}.{Project} could not be retrieved",
                issuer.Name,
                organization,
                project);
            return new KeyState(null, now, exception);
        }
    }

    private IReadOnlyList<SecurityKey>? LoadStoredKeys(string organization, string project, FederatedIssuer issuer)
    {
        var keys = string.IsNullOrEmpty(issuer.Jwks) ? null : ParseKeySet(issuer.Jwks);

        if (keys is null)
        {
            LogMisconfiguration(organization, project, issuer, "its stored JWKS is not a JSON Web Key Set");
        }

        return keys;
    }

    private async Task<IReadOnlyList<SecurityKey>?> DiscoverKeysAsync(
        string organization,
        string project,
        FederatedIssuer issuer,
        CancellationToken cancellationToken)
    {
        var allowPrivateNetworks = _options.Value.AllowPrivateNetworks;

        // Checked again at fetch time: the deployment may have stopped allowing private networks since the issuer was stored.
        if (FederationValidation.ValidateMetadataUrl(issuer.Issuer, allowPrivateNetworks, allowQuery: false) is { } issuerError)
        {
            LogMisconfiguration(organization, project, issuer, $"the issuer cannot be discovered: {issuerError}");
            return null;
        }

        var baseUrl = issuer.Issuer.EndsWith('/') ? issuer.Issuer[..^1] : issuer.Issuer;
        var document = await GetJsonAsync($"{baseUrl}/.well-known/openid-configuration", cancellationToken);

        // The discovery document speaks for exactly this issuer, or someone else's keys would verify its tokens.
        if (!string.Equals(GetString(document, "issuer"), issuer.Issuer, StringComparison.Ordinal))
        {
            LogMisconfiguration(organization, project, issuer, $"its discovery document names the issuer {GetString(document, "issuer") ?? "(none)"}");
            return null;
        }

        var jwksUri = GetString(document, "jwks_uri");

        if (FederationValidation.ValidateMetadataUrl(jwksUri, allowPrivateNetworks) is { } jwksError)
        {
            LogMisconfiguration(organization, project, issuer, $"the jwks_uri {jwksUri ?? "(none)"} of its discovery document is not allowed: {jwksError}");
            return null;
        }

        return await FetchKeySetAsync(organization, project, issuer, jwksUri, cancellationToken);
    }

    private async Task<IReadOnlyList<SecurityKey>?> FetchKeySetAsync(
        string organization,
        string project,
        FederatedIssuer issuer,
        string? jwksUri,
        CancellationToken cancellationToken)
    {
        if (FederationValidation.ValidateMetadataUrl(jwksUri, _options.Value.AllowPrivateNetworks) is { } error)
        {
            LogMisconfiguration(organization, project, issuer, $"the JWKS URI {jwksUri ?? "(none)"} is not allowed: {error}");
            return null;
        }

        var content = await GetAsync(jwksUri!, cancellationToken);
        return ParseKeySet(Encoding.UTF8.GetString(content))
            ?? throw new BearerKeyRetrievalException($"The JWKS {jwksUri} is not a JSON Web Key Set.");
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var content = await GetAsync(url, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(content);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : throw new BearerKeyRetrievalException($"The document {url} is not a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new BearerKeyRetrievalException($"The document {url} is not JSON.", exception);
        }
    }

    private async Task<byte[]> GetAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(FederationMetadataHttpHandler.HttpClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FetchTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            // Redirects are not followed, so a 3xx fails here too.
            if (!response.IsSuccessStatusCode)
            {
                throw new BearerKeyRetrievalException($"The request for {url} was answered with {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > FederationMetadataHttpHandler.MaxResponseBytes)
            {
                throw new BearerKeyRetrievalException($"The response for {url} is larger than {FederationMetadataHttpHandler.MaxResponseBytes} bytes.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await ReadLimitedAsync(stream, FederationMetadataHttpHandler.MaxResponseBytes, timeout.Token)
                ?? throw new BearerKeyRetrievalException($"The response for {url} is larger than {FederationMetadataHttpHandler.MaxResponseBytes} bytes.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // OperationCanceledException without a cancelled request is FetchTimeout or the client timeout.
            throw new BearerKeyRetrievalException($"The request for {url} failed.", exception);
        }
    }

    private void LogMisconfiguration(string organization, string project, FederatedIssuer issuer, string reason)
    {
        _logger.LogWarning(
            "The trusted issuer {IssuerName} ({Issuer}) of {Organization}.{Project} is misconfigured and refuses every token: {Reason}",
            issuer.Name,
            issuer.Issuer,
            organization,
            project,
            reason);
    }

    // Keys: the fetched keys, or null while the issuer is misconfigured (Failure null) or unreachable (Failure set).
    private sealed record KeyState(FederatedIssuerKeys? Keys, DateTimeOffset CheckedAt, BearerKeyRetrievalException? Failure);

    private sealed class KeyCacheEntry
    {
        private volatile KeyState? _state;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public KeyState? State
        {
            get => _state;
            set => _state = value;
        }
    }
}
