using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Token, discovery and GitHub Actions ID token requests: their own HttpClient, which follows no redirect (a secret is in
// the body, or in the header of the ID token request) and sends no Authorization, except the request token on the ID
// token request only; behind the resilience pipeline with POST retries, as a token request has no side effect.
internal sealed class TokenEndpointClient : IDisposable
{
    private const string BearerScheme = "Bearer";

    private static readonly TimeSpan FallbackTokenLifetime = TimeSpan.FromMinutes(5);

    // Longer lifetimes are cut: a huge value would overflow, and a day is long enough.
    private static readonly long MaxTokenLifetimeSeconds = (long)TimeSpan.FromDays(1).TotalSeconds;

    private readonly Lazy<HttpClient> _httpClient;
    private readonly TimeProvider _time;

    // The message of the last transient failure, named when the circuit is open: the answers that opened it are gone.
    private string? _lastFailure;

    public TokenEndpointClient(
        Action<SocketsHttpHandler>? configureHandler,
        StorytellerResilienceOptions? resilience,
        TimeProvider time,
        HttpMessageHandler? primaryHandler)
    {
        _time = time;

        // A copy: the caller's options may change later, and token requests always retry a POST.
        var options = resilience?.Clone() ?? new StorytellerResilienceOptions();
        options.RetryUnsafeMethods = true;

        // A primary handler of the caller (the seam of tests) belongs to the caller; it is not disposed with the client.
        _httpClient = new Lazy<HttpClient>(() => new HttpClient(
            new ResilienceHandler(StorytellerResilience.CreatePipeline(options))
            {
                InnerHandler = primaryHandler ?? CreatePrimaryHandler(configureHandler),
            },
            disposeHandler: primaryHandler is null)
        {
            // Attempt timeouts and the caller's token bound a request.
            Timeout = Timeout.InfiniteTimeSpan,
        });
    }

    // Closes the connections of token and discovery requests; a client that never sent one holds none.
    public void Dispose()
    {
        if (_httpClient.IsValueCreated)
        {
            _httpClient.Value.Dispose();
        }
    }

    public async Task<AccessToken> RequestTokenAsync(Uri tokenUrl, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        var target = StorytellerUrlRule.Describe(tokenUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(request, $"The token request to {target}", cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, $"The token request to {target}");

        return ReadToken(body)
            ?? throw new StorytellerAuthenticationException(
                StorytellerAuthenticationFailureReason.Rejected,
                $"The token endpoint {target} returned no bearer token.");
    }

    // GET {requestUrl}&audience={audience}: the ID token of a GitHub Actions job with permissions: id-token: write. The
    // request token goes on this request only, never to the token endpoint; messages name the runner without its query.
    public async Task<string> RequestGitHubIdTokenAsync(Uri requestUrl, string requestToken, string audience, CancellationToken cancellationToken)
    {
        var target = StorytellerUrlRule.Describe(requestUrl);

        // As written: the runner's URL has a query already (api-version=2.0).
        var separator = requestUrl.Query.Length > 0 ? "&" : "?";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{requestUrl.OriginalString}{separator}audience={Uri.EscapeDataString(audience)}", UriKind.Absolute));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, requestToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(request, $"The GitHub Actions ID token request to {target}", cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, $"The GitHub Actions ID token request to {target}");

        return ReadGitHubIdToken(body)
            ?? throw new StorytellerAuthenticationException(
                StorytellerAuthenticationFailureReason.Rejected,
                $"The GitHub Actions ID token request to {target} returned no token.");
    }

    // GET {endpoint}/v1/auth/configuration, anonymous: it describes the sign-in provider of the platform.
    public async Task<SignInDiscovery> DiscoverSignInAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var discoveryUrl = new Uri($"{StorytellerUrlRule.TrimBase(endpoint)}/v1/auth/configuration", UriKind.Absolute);
        var target = StorytellerUrlRule.Describe(discoveryUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, discoveryUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(request, $"The sign-in discovery at {target}", cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, $"The sign-in discovery at {target}");

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                // The API keeps PascalCase names.
                return new SignInDiscovery(
                    ReadString(root, "Provider") ?? ReadString(root, "provider"),
                    ReadString(root, "AuthKitDomain") ?? ReadString(root, "authKitDomain"));
            }
        }
        catch (JsonException)
        {
            // Reported below, like a body that is not an object.
        }

        throw new StorytellerAuthenticationException(
            StorytellerAuthenticationFailureReason.Rejected,
            $"The sign-in discovery at {target} returned no configuration.");
    }

    internal static SocketsHttpHandler CreatePrimaryHandler(Action<SocketsHttpHandler>? configure)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        configure?.Invoke(handler);

        // Again after the callback: a proxy setting must not turn redirects back on.
        handler.AllowAutoRedirect = false;
        return handler;
    }

    // error and error_description of an OAuth answer, or the message of an ErrorResponse; never the rest of the body.
    private static string DescribeOAuthError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var error = ReadString(root, "error");
            var description = (ReadString(root, "error_description") ?? ReadString(root, "message") ?? ReadString(root, "Message"))?.TrimEnd('.');

            return (error, description) switch
            {
                (not null, not null) => $"{error}: {description}",
                (not null, null) => error,
                (null, not null) => description,
                _ => string.Empty,
            };
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    // { "count": 1, "value": "<jwt>" }
    private static string? ReadGitHubIdToken(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadString(document.RootElement, "value") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static long? ReadLifetime(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var value))
        {
            return null;
        }

        // Some providers send the number as a string.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var seconds) => seconds,
            JsonValueKind.String when long.TryParse(value.GetString(), out var seconds) => seconds,
            _ => null,
        };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.Value.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenCircuitException exception)
        {
            var last = Volatile.Read(ref _lastFailure);
            throw new StorytellerAuthenticationException(
                StorytellerAuthenticationFailureReason.Unavailable,
                last is null ? $"{what} failed: {exception.Message}" : $"{what} failed: {exception.Message} The last failure: {last}",
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or ExecutionRejectedException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw Unavailable($"{what} failed: {exception.Message}", exception);
        }
    }

    private void EnsureSuccess(HttpResponseMessage response, string body, string what)
    {
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = DescribeOAuthError(body);
        var suffix = detail.Length > 0 ? $": {detail}" : string.Empty;

        if (status is 408 or 429 or >= 500)
        {
            throw Unavailable($"{what} failed with {status}{suffix}.", null);
        }

        // A redirect is never followed: the body carries a secret.
        throw new StorytellerAuthenticationException(
            StorytellerAuthenticationFailureReason.Rejected,
            status is >= 300 and < 400
                ? $"{what} was answered with a redirect ({status}), which is not followed."
                : $"{what} was rejected with {status}{suffix}.");
    }

    private StorytellerAuthenticationException Unavailable(string message, Exception? innerException)
    {
        Volatile.Write(ref _lastFailure, message);
        return new StorytellerAuthenticationException(StorytellerAuthenticationFailureReason.Unavailable, message, innerException);
    }

    private AccessToken? ReadToken(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || ReadString(root, "access_token") is not { } accessToken
                || (ReadString(root, "token_type") is { } tokenType && !string.Equals(tokenType, BearerScheme, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var now = _time.GetUtcNow();
            var lifetime = ReadLifetime(root);
            var expiresAt = lifetime > 0
                ? now.AddSeconds(Math.Min(lifetime.Value, MaxTokenLifetimeSeconds))
                : JwtExpiry.Read(accessToken) ?? now + FallbackTokenLifetime;
            return new AccessToken(accessToken, now, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
