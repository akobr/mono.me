using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// The Storyteller token for a person through the API's user token exchange (RFC 8693, POST {BaseUrl}/v1/access/token).
// The person's MCP token is the subject_token, Mcp:Actor:Id the client_id, and McpActorCredential adds the proof. The
// answer is a short-lived Storyteller user token that names the person and the MCP server (act).
// - One exchange per token of a person: answers are cached by the SHA-256 of the subject token until a minute before
//   they expire, or until shortly before they expire when they expire within the minute (they cannot outlive the
//   subject token), in a bounded cache of their own (CacheSizeLimit), and concurrent calls for one token share one
//   exchange.
// - Its own HttpClient (HttpClientName) with no retries, since every attempt counts against the API's replay limit per
//   subject token, and no redirects. No Authorization header: the proof is in the form.
// - Refusals become sign-in exceptions with fixed texts; the API's error_description only for invalid_grant, where its
//   texts are fixed too. Logs carry the status and the error code: never a token or a body.
public sealed class StorytellerTokenExchangeProvider : IStorytellerTokenProvider, IDisposable
{
    public const string HttpClientName = "StorytellerTokenExchange";
    public const string TokenPath = "v1/access/token";

    public const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    // Answers cached at once; each holds a token of a few kilobytes.
    public const long CacheSizeLimit = 10_000;

    // The longest error_description shown; the API's are one sentence.
    public const int MaxDescriptionLength = 300;

    // The longest answer read; a token response is a few kilobytes.
    public const int MaxResponseBytes = 64 * 1024;

    // An error code of an answer the server cannot read.
    public const string InvalidResponseCode = "invalid_response";

    public static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromSeconds(60);

    // The least time a cached answer is handed out with. The API exchanges no subject token with less (its
    // MinSubjectTokenLifetime) and accepts its own tokens with a clock skew.
    public static readonly TimeSpan MinRemainingLifetime = TimeSpan.FromSeconds(5);

    // The whole exchange, the actor's credential included. A caller that stops waiting does not stop it.
    public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly McpActorCredential _credential;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly string _tokenEndpoint;
    private readonly TimeSpan _exchangeTimeout;
    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<StorytellerToken>>> _inFlight = new(StringComparer.Ordinal);

    public StorytellerTokenExchangeProvider(
        IHttpClientFactory httpClientFactory,
        McpActorCredential credential,
        IOptions<StorytellerApiOptions> api,
        TimeProvider timeProvider,
        ILogger<StorytellerTokenExchangeProvider> logger)
        : this(httpClientFactory, credential, api, timeProvider, logger, ExchangeTimeout, CacheSizeLimit)
    {
    }

    // Tests give a shorter timeout and a smaller cache.
    internal StorytellerTokenExchangeProvider(
        IHttpClientFactory httpClientFactory,
        McpActorCredential credential,
        IOptions<StorytellerApiOptions> api,
        TimeProvider timeProvider,
        ILogger<StorytellerTokenExchangeProvider> logger,
        TimeSpan exchangeTimeout,
        long cacheSizeLimit)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _timeProvider = timeProvider;
        _logger = logger;
        _tokenEndpoint = GetTokenEndpoint(api.Value.BaseUrl);
        _exchangeTimeout = exchangeTimeout;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = cacheSizeLimit });
    }

    // Where the exchange is posted, and the default aud of client assertions (Mcp:Actor:ClientAssertionKey:Audience),
    // which the API compares with its own {PublicBaseUrl}/v1/access/token or its issuer.
    public static string GetTokenEndpoint(string baseUrl)
    {
        return $"{baseUrl.TrimEnd('/')}/{TokenPath}";
    }

    public async Task<StorytellerToken> GetTokenAsync(string userToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userToken);

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userToken)));

        if (_cache.TryGetValue(key, out CachedToken? cached) && cached!.RefreshAt > _timeProvider.GetUtcNow())
        {
            return cached.Token;
        }

        // The first caller starts the exchange and the others join it. No caller's cancellation stops it: the answer is
        // cached for whoever asks next. A failure is shared by the callers that joined, and never cached.
        var exchange = _inFlight.GetOrAdd(key, _ => new Lazy<Task<StorytellerToken>>(() => ExchangeAndCacheAsync(key, userToken)));
        return await exchange.Value.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    // The error of an OAuth answer when it is a plain code (RFC 6749 names them in lower case with underscores).
    private static string? GetErrorCode(JsonElement answer)
    {
        return GetString(answer, "error") is { Length: > 0 and <= 64 } error && error.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
            ? error
            : null;
    }

    // The error_description without control characters and at most MaxDescriptionLength long; null when empty.
    private static string? GetDescription(JsonElement? answer)
    {
        if (answer is not { } value || GetString(value, "error_description") is not { } description)
        {
            return null;
        }

        var text = new string(description.Where(character => !char.IsControl(character)).ToArray()).Trim();

        if (text.Length > MaxDescriptionLength)
        {
            text = text[..MaxDescriptionLength].TrimEnd();
        }

        return text.Length == 0 ? null : text;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static JsonDocument? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // At most MaxResponseBytes; null for a longer answer.
    private static async Task<string?> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private async Task<StorytellerToken> ExchangeAndCacheAsync(string key, string userToken)
    {
        try
        {
            var token = await ExchangeAsync(userToken);
            var now = _timeProvider.GetUtcNow();

            // A minute before it expires. An answer that expires within the minute is bound by the subject token's exp,
            // which no new exchange can outlive, so it is kept until shortly before its own exp. Otherwise every call
            // would be an exchange counted against the API's replay limit.
            var refreshAt = token.ExpiresAt - RefreshBeforeExpiry > now
                ? token.ExpiresAt - RefreshBeforeExpiry
                : token.ExpiresAt - MinRemainingLifetime;

            // An answer with MinRemainingLifetime or less left serves the calls that wait for it only.
            if (refreshAt > now)
            {
                _cache.Set(key, new CachedToken(token, refreshAt), new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = refreshAt - now,
                });
            }

            return token;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<StorytellerToken> ExchangeAsync(string userToken)
    {
        using var timeout = new CancellationTokenSource(_exchangeTimeout);
        int status;
        string? body;

        try
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", GrantType),
                new("subject_token", userToken),
                new("subject_token_type", AccessTokenType),
                new("client_id", _credential.ActorId),
            };
            form.AddRange(await _credential.GetProofAsync(_tokenEndpoint, timeout.Token));

            using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            status = (int)response.StatusCode;
            body = await ReadBodyAsync(response.Content, timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // No caller's cancellation reaches here: a cancellation is the timeout.
            _logger.LogWarning("The token exchange with the Storyteller API did not finish ({ExceptionType}).", exception.GetType().Name);
            throw new StorytellerSignInUnavailableException();
        }

        return ReadAnswer(status, body);
    }

    private StorytellerToken ReadAnswer(int status, string? body)
    {
        using var document = TryParse(body);
        var answer = document?.RootElement;

        if (status == 200)
        {
            if (answer is { } success && TryReadToken(success, out var token))
            {
                return token;
            }

            _logger.LogWarning("The Storyteller API answered the token exchange with no token the MCP server can read.");
            throw new StorytellerSignInFailedException(InvalidResponseCode);
        }

        var error = answer is { } refusal ? GetErrorCode(refusal) : null;

        // A sign-in the API refuses for another reason than a missing account: when it refuses every person, the API's
        // actor usually lacks an aud this server accepts (McpAuthSettings.ValidAudiences, logged at start). The API's
        // own log names the reason; no token or description is logged here.
        if (status is 400 or 401
            && error == StorytellerSignInRefusedException.ErrorCodeValue
            && !string.Equals(GetDescription(answer), StorytellerSignInRefusedException.NoAccountMessage, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "The Storyteller API refused the token exchange: {Status} {Error}. If it refuses every sign-in, check that the API's Auth:TokenExchange:Actors entry '{ActorId}' lists in SubjectAudiences every audience this server accepts (logged at start).",
                status,
                error,
                _credential.ActorId);
        }
        else
        {
            _logger.LogWarning("The Storyteller API refused the token exchange: {Status} {Error}.", status, error ?? "(no error code)");
        }

        if (status is 502 or 503 or 504 || error == StorytellerSignInUnavailableException.ErrorCodeValue)
        {
            throw new StorytellerSignInUnavailableException();
        }

        // RFC 6749 section 5.2: 400, or 401 for a client that authenticated with a header.
        if (status is 400 or 401)
        {
            switch (error)
            {
                case StorytellerSignInRefusedException.ErrorCodeValue:
                    throw new StorytellerSignInRefusedException(GetDescription(answer));
                case "invalid_client":
                    throw new StorytellerActorException(StorytellerActorException.ActorNotAcceptedMessage, error);
                case "unsupported_grant_type":
                    throw new StorytellerActorException(StorytellerActorException.ExchangeDisabledMessage, error);
            }
        }

        throw new StorytellerSignInFailedException(error ?? $"http_{status}");
    }

    // access_token and a positive expires_in are required; token_type, when present, is Bearer. restrictions decides
    // which tools the server offers, so a malformed one makes the answer unreadable rather than unrestricted.
    private bool TryReadToken(JsonElement answer, out StorytellerToken token)
    {
        token = null!;

        if (GetString(answer, "access_token") is not { Length: > 0 } accessToken
            || !answer.TryGetProperty("expires_in", out var expiresIn)
            || expiresIn.ValueKind != JsonValueKind.Number
            || !expiresIn.TryGetInt32(out var seconds)
            || seconds <= 0)
        {
            return false;
        }

        if (answer.TryGetProperty("token_type", out var tokenType)
            && !string.Equals(tokenType.ValueKind == JsonValueKind.String ? tokenType.GetString() : null, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var restrictions = new HashSet<string>(StringComparer.Ordinal);

        // The API always lists them, an empty array when there are none; a missing member fails closed, as the default of
        // every actor.
        if (!answer.TryGetProperty("restrictions", out var listed))
        {
            restrictions.Add(StorytellerToken.NoSecretReveal);
        }
        else
        {
            if (listed.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var restriction in listed.EnumerateArray())
            {
                if (restriction.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(restriction.GetString()))
                {
                    return false;
                }

                restrictions.Add(restriction.GetString()!);
            }
        }

        token = new StorytellerToken(accessToken, _timeProvider.GetUtcNow().AddSeconds(seconds), GetString(answer, "scope") ?? string.Empty, restrictions);
        return true;
    }

    private sealed record CachedToken(StorytellerToken Token, DateTimeOffset RefreshAt);
}
