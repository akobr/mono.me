using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Reading;

// The one reader of the provider and the document service: a conditional read in the identity's mode, with the tag
// stored for that identity, classified into an outcome. Holds no state, so concurrent reads are fine.
internal sealed class DocumentReader
{
    // More paths than this are counted, not listed.
    private const int MaxListedPaths = 20;

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
            return ReadFailures.Classify(exception, identity.Key, identity.Endpoint, Describe);
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

    // The classification lives in ReadFailures; kept here for the tests of the reader.
    internal static bool IsTransientStatus(int status)
    {
        return ReadFailures.IsTransientStatus(status);
    }

    // The words of an answer to a configuration read; ReadFailures adds the status, the error code and the platform's
    // message and hint.
    private static string Describe(string key, int status, string? errorCode)
    {
        return status switch
        {
            404 when errorCode == "AnnotationDeleted" => $"The annotation of {key} is deleted",
            400 => $"The platform does not accept the key {key}",
            401 => $"The platform refused the credentials of the read of {key}; set the variables of a machine the project knows, or a credential in code",
            403 => $"The machine may not read {key}; it needs Configuration.Read on the project, and an annotation-restricted machine reads its own annotation only",
            404 => $"The configuration {key} does not exist",
            422 => $"A binding in {key} cannot be evaluated",
            >= 300 and < 400 => $"Reading {key} was redirected, which is not followed; set the endpoint to the final URL",
            _ when ReadFailures.IsSuccessStatus(status) => $"The answer to the read of {key} did not parse as a configuration",
            _ => $"Reading {key} failed",
        };
    }
}
