using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace _42.Platform.Storyteller.Sdk.Configuring.Reading;

// The one reader of the provider and the document service: a conditional read in the identity's mode, with the tag
// stored for that identity, classified into an outcome. Holds no state, so concurrent reads are fine.
internal sealed class DocumentReader
{
    // More paths than this are counted, not listed.
    private const int MaxListedPaths = 20;

    // A proxy, a captive portal or an API that does not match the SDK. The SDK's message and the body are not quoted:
    // the body of a resolved read may hold secrets.
    private const string UnparsedAnswerFix = ": check that the endpoint is the Storyteller API base, /api included, and that nothing in between rewrites the response.";

    private readonly IConfigurationsApiClient _client;
    private readonly ILogger _logger;

    public DocumentReader(IConfigurationsApiClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
    }

    // Only the cancellation of cancellationToken, and an ObjectDisposedException of a transport disposed by a stop,
    // escape; the caller tells a stop from its ReadTimeout. Everything else becomes an outcome.
    public async Task<ReadOutcome> ReadAsync(DocumentIdentity identity, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        ConditionalResponse<Configuration> response;

        try
        {
            response = identity.Mode == StorytellerReadMode.Resolved
                ? await _client.GetResolvedConfigurationConditionalAsync(
                    identity.Organization, identity.Project, identity.View, identity.AnnotationKey, ifNoneMatch, cancellationToken).ConfigureAwait(false)
                : await _client.GetConfigurationConditionalAsync(
                    identity.Organization, identity.Project, identity.View, identity.AnnotationKey, ifNoneMatch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Classify(identity, exception);
        }

        // The header depends on the caller's rights, not on the content: a document without secrets is withheld too.
        if (identity.Mode == StorytellerReadMode.Resolved && response.SecretsWithheld is { } reason)
        {
            return new ReadOutcome.Withheld(
                reason,
                response.StatusCode,
                $"The platform withheld the secrets of {identity.Key} ({reason}): the machine may read secrets, but it presented no client certificate. Present the machine certificate (STORYTELLER_CLIENT_CERTIFICATE), or remove Secrets.Read from the machine.");
        }

        return response.IsNotModified
            ? new ReadOutcome.NotModified(response.ETag ?? ifNoneMatch, response.CacheControl)
            : new ReadOutcome.Changed(response.Result!, response.ETag, response.CacheControl);
    }

    // One warning per applied document of a resolved read, with the paths of the strings that start with '@'.
    public void WarnUnboundBindings(DocumentIdentity identity, JToken? content)
    {
        if (identity.Mode != StorytellerReadMode.Resolved || !_logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        var paths = UnboundBindings.FindPaths(content);

        if (paths.Count == 0)
        {
            return;
        }

        var listed = string.Join(", ", paths.Take(MaxListedPaths));

        if (paths.Count > MaxListedPaths)
        {
            listed += ", …";
        }

        _logger.LogWarning(
            "The configuration {Key} has {Count} string values that start with '@', at {Paths}: bindings the platform did not resolve for this caller, usually because the machine may not read secrets. They are applied as written.",
            identity.Key,
            paths.Count,
            listed);
    }

    // 408, 424, 429 and 5xx other than 501 and 505 are worth another try; 501, 505 and the other 4xx get the same answer
    // again, as in the SDK's resilience pipeline. A 2xx that did not parse came through something in between; a 3xx is
    // a redirect, which is not followed.
    internal static bool IsTransientStatus(int status)
    {
        return status switch
        {
            408 or 424 or 429 => true,
            501 or 505 => false,
            >= 500 => true,
            >= 200 and < 300 => true,
            _ => false,
        };
    }

    private static ReadOutcome Classify(DocumentIdentity identity, Exception exception)
    {
        switch (exception)
        {
            case ApiException api:
                return ClassifyStatus(identity, api);

            case StorytellerAuthenticationException authentication:
                var credentialMessage = $"The credential could not authorize the read of {identity.Key} ({authentication.Reason}): {authentication.Message}";
                return authentication.IsTransient
                    ? new ReadOutcome.Transient(null, null, credentialMessage, authentication)
                    : new ReadOutcome.Definitive(null, null, credentialMessage, authentication);

            case BrokenCircuitException:
                return new ReadOutcome.Transient(
                    null,
                    null,
                    $"Reading {identity.Key} was not attempted: the circuit to {identity.Endpoint} is open after repeated failures.",
                    exception);

            case TimeoutRejectedException timeout:
                return new ReadOutcome.Transient(null, null, $"Reading {identity.Key} timed out after {timeout.Timeout}.", exception);

            case HttpRequestException http:
                return new ReadOutcome.Transient((int?)http.StatusCode, null, $"Reading {identity.Key} from {identity.Endpoint} failed: {http.Message}", exception);

            // A connect timeout of the handler, which the caller did not request.
            case OperationCanceledException:
                return new ReadOutcome.Transient(null, null, $"Reading {identity.Key} from {identity.Endpoint} timed out.", exception);

            // Unknown, as from a credential of the application: no verdict of the platform, so the cache and the poller
            // carry on. Its message stays in the inner exception; it is not ours to vouch for.
            default:
                return new ReadOutcome.Transient(null, null, $"Reading {identity.Key} failed with {exception.GetType().Name}.", exception);
        }
    }

    private static ReadOutcome ClassifyStatus(DocumentIdentity identity, ApiException exception)
    {
        var status = exception.StatusCode;
        var error = (exception as ApiException<ErrorResponse>)?.Result ?? (status >= 400 ? TryReadError(exception.Response) : null);
        var errorCode = string.IsNullOrWhiteSpace(error?.ErrorCode) ? null : error.ErrorCode;
        var message = $"{Describe(identity, status, errorCode)} ({status}{(errorCode is null ? string.Empty : $" {errorCode}")}){(IsSuccessStatus(status) ? UnparsedAnswerFix : DescribeError(error))}";

        return IsTransientStatus(status)
            ? new ReadOutcome.Transient(status, errorCode, message, exception)
            : new ReadOutcome.Definitive(status, errorCode, message, exception);
    }

    private static string Describe(DocumentIdentity identity, int status, string? errorCode)
    {
        return status switch
        {
            404 when errorCode == "AnnotationDeleted" => $"The annotation of {identity.Key} is deleted",
            400 => $"The platform does not accept the key {identity.Key}",
            401 => $"The platform refused the credentials of the read of {identity.Key}; set the variables of a machine the project knows, or a credential in code",
            403 => $"The machine may not read {identity.Key}; it needs Configuration.Read on the project, and an annotation-restricted machine reads its own annotation only",
            404 => $"The configuration {identity.Key} does not exist",
            422 => $"A binding in {identity.Key} cannot be evaluated",
            >= 300 and < 400 => $"Reading {identity.Key} was redirected, which is not followed; set the endpoint to the final URL",
            _ when IsSuccessStatus(status) => $"The answer to the read of {identity.Key} did not parse as a configuration",
            _ => $"Reading {identity.Key} failed",
        };
    }

    private static bool IsSuccessStatus(int status)
    {
        return status is >= 200 and < 300;
    }

    // The statuses the OpenAPI document does not declare (424, 503, …) still carry an ErrorResponse; the generated
    // client leaves it as text.
    private static ErrorResponse? TryReadError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<ErrorResponse>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The platform's ErrorResponse holds no secret.
    private static string DescribeError(ErrorResponse? error)
    {
        var parts = new[] { error?.Message, error?.Hint }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim().TrimEnd('.'))
            .ToList();

        return parts.Count == 0 ? "." : $": {string.Join(". ", parts)}.";
    }
}
