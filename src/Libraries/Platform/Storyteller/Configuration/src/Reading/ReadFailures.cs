using _42.Platform.Storyteller.Sdk.Credentials;
using Newtonsoft.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace _42.Platform.Storyteller.Sdk.Configuring.Reading;

// The classification of a failed read of the platform, shared by the reads of configurations and Hosting's reads of
// annotations: the status and error code of an answer, a credential that could not authorize, a timeout, an open
// circuit or a network error becomes a Transient or a Definitive outcome. The kind of read only words its answers, with
// a describer of a status and an error code; everything else reads the same for every kind.
internal static class ReadFailures
{
    // A proxy, a captive portal or an API that does not match the SDK. The SDK's message and the body are not quoted:
    // the body of a resolved read may hold secrets.
    private const string UnparsedAnswerFix = ": check that the endpoint is the Storyteller API base, /api included, and that nothing in between rewrites the response.";

    // 408, 424, 429 and 5xx other than 501 and 505 are worth another try; 501, 505 and the other 4xx get the same answer
    // again, as in the SDK's resilience pipeline. A 2xx that did not parse came through something in between; a 3xx is
    // a redirect, which is not followed.
    public static bool IsTransientStatus(int status)
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

    // key and endpoint: as the messages name them. describeAnswer: the phrase of an answer of the platform for a key, a
    // status and an error code; the status, the error code and the platform's message and hint follow it.
    public static ReadOutcome.Failure Classify(Exception exception, string key, string endpoint, Func<string, int, string?, string> describeAnswer)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(describeAnswer);

        switch (exception)
        {
            case ApiException api:
                return ClassifyStatus(api, key, describeAnswer);

            case StorytellerAuthenticationException authentication:
                var credentialMessage = $"The credential could not authorize the read of {key} ({authentication.Reason}): {authentication.Message}";
                return authentication.IsTransient
                    ? new ReadOutcome.Transient(null, null, credentialMessage, authentication)
                    : new ReadOutcome.Definitive(null, null, credentialMessage, authentication);

            case BrokenCircuitException:
                return new ReadOutcome.Transient(
                    null,
                    null,
                    $"Reading {key} was not attempted: the circuit to {endpoint} is open after repeated failures.",
                    exception);

            case TimeoutRejectedException timeout:
                return new ReadOutcome.Transient(null, null, $"Reading {key} timed out after {timeout.Timeout}.", exception);

            case HttpRequestException http:
                return new ReadOutcome.Transient((int?)http.StatusCode, null, $"Reading {key} from {endpoint} failed: {http.Message}", exception);

            // A connect timeout of the handler, which the caller did not request.
            case OperationCanceledException:
                return new ReadOutcome.Transient(null, null, $"Reading {key} from {endpoint} timed out.", exception);

            // Unknown, as from a credential of the application: no verdict of the platform, so the cache and the poller
            // carry on. Its message stays in the inner exception; it is not ours to vouch for.
            default:
                return new ReadOutcome.Transient(null, null, $"Reading {key} failed with {exception.GetType().Name}.", exception);
        }
    }

    public static bool IsSuccessStatus(int status)
    {
        return status is >= 200 and < 300;
    }

    private static ReadOutcome.Failure ClassifyStatus(ApiException exception, string key, Func<string, int, string?, string> describeAnswer)
    {
        var status = exception.StatusCode;
        var error = (exception as ApiException<ErrorResponse>)?.Result ?? (status >= 400 ? TryReadError(exception.Response) : null);
        var errorCode = string.IsNullOrWhiteSpace(error?.ErrorCode) ? null : error.ErrorCode;
        var message = $"{describeAnswer(key, status, errorCode)} ({status}{(errorCode is null ? string.Empty : $" {errorCode}")}){(IsSuccessStatus(status) ? UnparsedAnswerFix : DescribeError(error))}";

        return IsTransientStatus(status)
            ? new ReadOutcome.Transient(status, errorCode, message, exception)
            : new ReadOutcome.Definitive(status, errorCode, message, exception);
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
