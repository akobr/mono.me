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

// Token, discovery, GitHub Actions ID token and managed identity token requests: their own HttpClient, which follows no
// redirect (a secret is in the body, or in a header of the ID token and managed identity requests) and sends no
// Authorization, except the request token on the ID token request only; behind the resilience pipeline with POST
// retries, as a token request has no side effect. The managed identity request has a second client, which uses no proxy.
internal sealed class TokenEndpointClient : IDisposable
{
    private const string BearerScheme = "Bearer";

    // What a failed managed identity request asks: the two settings that decide whether the host answers with a token.
    private const string ManagedIdentityHint =
        "Does the host have a managed identity, and is the resource the identifier URI of the Storyteller app registration (api://<appId>)?";

    private static readonly TimeSpan FallbackTokenLifetime = TimeSpan.FromMinutes(5);

    // Longer lifetimes are cut: a huge value would overflow, and a day is long enough.
    private static readonly long MaxTokenLifetimeSeconds = (long)TimeSpan.FromDays(1).TotalSeconds;

    private readonly Lazy<HttpClient> _httpClient;

    // The client of the managed identity request, without a proxy: the endpoint is on the host or its network, where a
    // proxy cannot reach it, and over http a proxy would get the header of the host.
    private readonly Lazy<HttpClient> _directHttpClient;
    private readonly TimeProvider _time;

    // How long the body of an answer may take after its headers: the attempt timeout again, none when that is off. The
    // attempt timeout of the pipeline ends with the headers, so an answer that stalls after them would never end for a
    // caller without a token of its own.
    private readonly TimeSpan? _answerTimeout;

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
        _answerTimeout = options.AttemptTimeout > TimeSpan.Zero && options.AttemptTimeout != Timeout.InfiniteTimeSpan ? options.AttemptTimeout : null;

        // One pipeline for both clients, so one circuit breaker: every request of the credential counts in it.
        var pipeline = new Lazy<ResiliencePipeline<HttpResponseMessage>>(() => StorytellerResilience.CreatePipeline(options));
        _httpClient = new Lazy<HttpClient>(() => CreateClient(pipeline.Value, primaryHandler, configureHandler, useProxy: true));
        _directHttpClient = new Lazy<HttpClient>(() => CreateClient(pipeline.Value, primaryHandler, configureHandler, useProxy: false));
    }

    // Closes the connections of token and discovery requests; a client that never sent one holds none.
    public void Dispose()
    {
        if (_httpClient.IsValueCreated)
        {
            _httpClient.Value.Dispose();
        }

        if (_directHttpClient.IsValueCreated)
        {
            _directHttpClient.Value.Dispose();
        }
    }

    public async Task<AccessToken> RequestTokenAsync(Uri tokenUrl, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        var target = StorytellerUrlRule.Describe(tokenUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(_httpClient.Value, request, $"The token request to {target}", cancellationToken).ConfigureAwait(false);
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

        using var response = await SendAsync(_httpClient.Value, request, $"The GitHub Actions ID token request to {target}", cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body, $"The GitHub Actions ID token request to {target}");

        // { "count": 1, "value": "<jwt>" }
        return ReadTokenValue(body, "value")
            ?? throw new StorytellerAuthenticationException(
                StorytellerAuthenticationFailureReason.Rejected,
                $"The GitHub Actions ID token request to {target} returned no token.");
    }

    // GET {endpoint}?api-version={version}&resource={resource}[&client_id={id}]: the access token of the managed identity
    // of an Azure host, for the resource. The header of the endpoint (the host's IDENTITY_HEADER, or Metadata: true of
    // IMDS) goes on this request only, never to the token endpoint and never through a proxy, and is in no message;
    // messages name the endpoint without its query, and the resource.
    public async Task<string> RequestAzureManagedIdentityTokenAsync(
        AzureManagedIdentityEndpoint endpoint,
        string resource,
        string? managedIdentityClientId,
        CancellationToken cancellationToken)
    {
        var what = $"The managed identity token request to {StorytellerUrlRule.Describe(endpoint.Url)} for the resource {resource}";

        // As written: IDENTITY_ENDPOINT is the platform's.
        var separator = endpoint.Url.Query.Length > 0 ? "&" : "?";
        var clientId = managedIdentityClientId is null ? string.Empty : $"&client_id={Uri.EscapeDataString(managedIdentityClientId)}";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"{endpoint.Url.OriginalString}{separator}api-version={endpoint.ApiVersion}&resource={Uri.EscapeDataString(resource)}{clientId}", UriKind.Absolute));
        request.Headers.TryAddWithoutValidation(endpoint.HeaderName, endpoint.HeaderValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await SendAsync(_directHttpClient.Value, request, what, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, body, what, endpoint.IsImds);

            return ReadTokenValue(body, "access_token")
                ?? throw new StorytellerAuthenticationException(
                    StorytellerAuthenticationFailureReason.Rejected,
                    $"{what} returned no token.");
        }
        catch (StorytellerAuthenticationException exception) when (exception.InnerException is not BrokenCircuitException)
        {
            // An open circuit passes as it is: it names the last failure, which can be the token endpoint's, so the
            // question about the host does not belong to it. The hint is a sentence of its own: the runtime's text of a
            // network failure may end without a period.
            throw new StorytellerAuthenticationException(exception.Reason, $"{exception.Message.TrimEnd('.')}. {ManagedIdentityHint}", exception);
        }
    }

    // GET {endpoint}/v1/auth/configuration, anonymous: it describes the sign-in provider of the platform.
    public async Task<SignInDiscovery> DiscoverSignInAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var discoveryUrl = new Uri($"{StorytellerUrlRule.TrimBase(endpoint)}/v1/auth/configuration", UriKind.Absolute);
        var target = StorytellerUrlRule.Describe(discoveryUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, discoveryUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await SendAsync(_httpClient.Value, request, $"The sign-in discovery at {target}", cancellationToken).ConfigureAwait(false);
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

    // useProxy false is the handler of the managed identity request.
    internal static SocketsHttpHandler CreatePrimaryHandler(Action<SocketsHttpHandler>? configure, bool useProxy = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        configure?.Invoke(handler);

        // Again after the callback: a proxy setting must not turn redirects back on.
        handler.AllowAutoRedirect = false;

        // After the callback too: the proxy it sets serves the token endpoint, never the managed identity request.
        if (!useProxy)
        {
            handler.UseProxy = false;
        }

        return handler;
    }

    // A primary handler of the caller (the seam of tests) serves both clients and belongs to the caller; it is not
    // disposed with a client.
    private static HttpClient CreateClient(
        ResiliencePipeline<HttpResponseMessage> pipeline,
        HttpMessageHandler? primaryHandler,
        Action<SocketsHttpHandler>? configureHandler,
        bool useProxy)
    {
        return new HttpClient(
            new ResilienceHandler(pipeline)
            {
                InnerHandler = primaryHandler ?? CreatePrimaryHandler(configureHandler, useProxy),
            },
            disposeHandler: primaryHandler is null)
        {
            // Attempt timeouts and the caller's token bound a request.
            Timeout = Timeout.InfiniteTimeSpan,
        };
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

    // The token of a platform's answer, trimmed: value of the GitHub Actions runner, access_token of a managed identity.
    private static string? ReadTokenValue(string body, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadString(document.RootElement, name) : null;
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

    // The answer with its body read: the callers read it from memory.
    private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, string what, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            try
            {
                await ReadBodyAsync(client, response, what, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
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

    // The body into memory, within the answer timeout: the caller's cancellation stays a cancellation, the timeout is
    // Unavailable. It is not retried: the pipeline is behind it.
    private async Task ReadBodyAsync(HttpClient client, HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (_answerTimeout is not { } timeout)
        {
            await response.Content.LoadIntoBufferAsync(client.MaxResponseContentBufferSize, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            await response.Content.LoadIntoBufferAsync(client.MaxResponseContentBufferSize, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable($"{what} failed: the body of its answer did not end within {timeout}.", exception);
        }
    }

    // isImds: IMDS answers 404 and 410 while it is updating, so they are worth another try later; a host without a
    // managed identity answers 400.
    private void EnsureSuccess(HttpResponseMessage response, string body, string what, bool isImds = false)
    {
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = DescribeOAuthError(body);
        var suffix = detail.Length > 0 ? $": {detail}" : string.Empty;

        if (status is 408 or 429 or >= 500 || (isImds && status is 404 or 410))
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
