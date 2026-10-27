using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// Point reads of annotations through the catalog's transport, for who am I and the catalog. Each read is bounded by
// ReadTimeout and classified with the provider's ReadFailures in the words of an annotation read. Holds no state, so
// concurrent reads are fine.
internal sealed class CatalogReader
{
    private const string AnnotationRestricted = "AnnotationRestricted";

    private readonly StorytellerSettings _settings;
    private readonly CatalogTransport _transport;
    private readonly string _endpoint;

    public CatalogReader(StorytellerSettings settings, CatalogTransport transport)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(transport);

        _settings = settings;
        _transport = transport;
        _endpoint = StorytellerUrlRule.Describe(settings.Endpoint)!.TrimEnd('/');
    }

    // A 404 without a body: the annotation is missing, purged, or deleted and not asked for.
    public static bool IsMissing(StorytellerConfigurationException exception)
    {
        return exception.StatusCode == 404 && exception.ErrorCode is null;
    }

    // A 403 of a machine restricted to another annotation.
    public static bool IsRestricted(StorytellerConfigurationException exception)
    {
        return exception.StatusCode == 403 && string.Equals(exception.ErrorCode, AnnotationRestricted, StringComparison.Ordinal);
    }

    // Always with includeInactive: a disabled, scheduled or expired annotation is an answer, not a failure. Throws
    // StorytellerConfigurationException; the cancellation of cancellationToken escapes, and a stop or a disposed
    // transport ends the read with ObjectDisposedException.
    public async Task<StorytellerAnnotation> ReadAsync(FullKey key, bool includeDeleted, CancellationToken stopping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var text = key.ToString();
        using var scope = new RunScope(_settings.ReadTimeout, stopping, cancellationToken);
        var read = _transport.Client.GetAnnotationAsync(
            key.OrganizationName,
            key.ProjectName,
            key.ViewName,
            key.Annotation.ToString(),
            includeInactive: true,
            includeDeleted: includeDeleted ? true : null,
            scope.Token);

        try
        {
            // A handler that ignores cancellation cannot hold the read past ReadTimeout.
            return AnnotationMapper.Map(key, await read.WaitAsync(scope.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Observe(read);
            throw;
        }
        catch (OperationCanceledException exception) when (scope.IsTimedOut)
        {
            Observe(read);
            throw new StorytellerConfigurationException(
                $"Reading {text} did not finish within the ReadTimeout of {_settings.ReadTimeout}.",
                text,
                null,
                null,
                true,
                exception);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
            && (stopping.IsCancellationRequested || _transport.IsDisposed))
        {
            Observe(read);
            throw new ObjectDisposedException("The Storyteller catalog was disposed.", exception);
        }
        catch (Exception exception)
        {
            throw ReadFailures.Classify(exception, text, _endpoint, Describe).ToException(text);
        }
    }

    // A task that nobody waits for any more.
    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // The words of an answer to an annotation read; ReadFailures adds the status, the error code and the platform's
    // message and hint.
    private static string Describe(string key, int status, string? errorCode)
    {
        return status switch
        {
            403 when errorCode == AnnotationRestricted => $"The machine may not read {key}: a machine restricted to an annotation reads that annotation and its descendants only",
            404 when errorCode == "AnnotationInactive" => $"The annotation {key} is not active",
            400 => $"The platform does not accept the key {key}",
            401 => $"The platform refused the credentials of the read of {key}; set the variables of a machine the project knows, or a credential in code",
            403 => $"The machine may not read {key}; it needs Annotation.Read on the project",
            404 => $"The annotation {key} does not exist",
            >= 300 and < 400 => $"Reading {key} was redirected, which is not followed; set the endpoint to the final URL",
            _ when ReadFailures.IsSuccessStatus(status) => $"The answer to the read of {key} did not parse as an annotation",
            _ => $"Reading {key} failed",
        };
    }
}
