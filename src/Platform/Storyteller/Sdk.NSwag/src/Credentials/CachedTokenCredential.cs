using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// A bearer token from a token request, kept in memory and renewed 60 s before it expires, or halfway through a token
// that lives less than two minutes. One gate, so concurrent callers share one request: those that waited for a request
// share its token, or its failure.
internal abstract class CachedTokenCredential : StorytellerCredential
{
    private const string BearerScheme = "Bearer";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TokenEndpointClient _client;
    private AccessToken? _token;

    // Token requests that ended with a token or a failure; a cancelled one does not count.
    private long _requestCount;

    // The failure of the last request that ended, null when it brought a token.
    private StorytellerAuthenticationException? _lastFailure;

    protected CachedTokenCredential(
        Action<SocketsHttpHandler>? configureHandler,
        TimeProvider? timeProvider,
        StorytellerCredentialBinding? binding,
        Func<string, string?> getVariable,
        HttpMessageHandler? primaryHandler)
    {
        Binding = binding;
        GetVariable = getVariable;
        PrimaryHandler = primaryHandler;
        Time = timeProvider ?? binding?.TimeProvider ?? TimeProvider.System;
        _client = new TokenEndpointClient(configureHandler ?? binding?.ConfigureHandler, binding?.Resilience, Time, primaryHandler);
    }

    public override bool CanRenew => true;

    protected StorytellerCredentialBinding? Binding { get; }

    protected Func<string, string?> GetVariable { get; }

    protected HttpMessageHandler? PrimaryHandler { get; }

    protected TimeProvider Time { get; }

    public override async ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
    {
        var token = Volatile.Read(ref _token);

        if (token is not null && !IsRejected(token, rejected) && IsFresh(token))
        {
            return token.Header;
        }

        var requestCount = Interlocked.Read(ref _requestCount);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            token = _token;

            // Dropped only if no newer token replaced it while this caller waited.
            if (token is not null && IsRejected(token, rejected))
            {
                Volatile.Write(ref _token, null);
                token = null;
            }

            if (token is not null && IsFresh(token))
            {
                return token.Header;
            }

            // A request ended while this caller waited: its token is shared even when it is not fresh, and a failed early
            // renewal shares the token the API still accepts.
            if (_requestCount != requestCount)
            {
                if (token is not null && (_lastFailure is null || IsUnexpired(token)))
                {
                    return token.Header;
                }

                if (_lastFailure is { } failure)
                {
                    throw new StorytellerAuthenticationException(failure.Reason, failure.Message, failure);
                }
            }

            try
            {
                var renewed = await RequestTokenAsync(_client, cancellationToken).ConfigureAwait(false);
                _lastFailure = null;
                Volatile.Write(ref _token, renewed);
                Interlocked.Increment(ref _requestCount);
                return renewed.Header;
            }
            catch (StorytellerAuthenticationException exception)
            {
                _lastFailure = exception;
                Interlocked.Increment(ref _requestCount);

                // An early renewal that failed keeps the token the API still accepts until it expires.
                if (IsUnexpired(token))
                {
                    return token!.Header;
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    protected abstract Task<AccessToken> RequestTokenAsync(TokenEndpointClient client, CancellationToken cancellationToken);

    // The binding's endpoint, or the environment's when unbound; the credential's own value wins over both.
    protected Uri ResolveEndpoint(Uri? own, string purpose)
    {
        var endpoint = own
            ?? (Binding is null ? UnboundTarget.ReadEndpoint(GetVariable) : Binding.Endpoint)
            ?? throw StorytellerAuthenticationException.Configuration(
                $"{purpose} needs the API endpoint: bind the credential, set the endpoint of its options, or set {StorytellerVariables.ENDPOINT}.");

        return LocalUrlRule.RequireEndpoint(endpoint, "The endpoint of the credential");
    }

    private static bool IsRejected(AccessToken token, AuthenticationHeaderValue? rejected)
    {
        return rejected is not null
            && string.Equals(rejected.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rejected.Parameter, token.Value, StringComparison.Ordinal);
    }

    // A token that lives less than twice the margin would never be fresh, and every call would request a new one.
    private bool IsFresh(AccessToken token)
    {
        var lifetime = token.ExpiresAt - token.IssuedAt;
        var margin = lifetime < 2 * RefreshMargin ? lifetime / 2 : RefreshMargin;
        return Time.GetUtcNow() < token.ExpiresAt - margin;
    }

    private bool IsUnexpired(AccessToken? token)
    {
        return token is not null && Time.GetUtcNow() < token.ExpiresAt;
    }
}
