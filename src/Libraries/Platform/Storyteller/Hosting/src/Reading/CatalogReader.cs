using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// Point reads and list pages of annotations through the catalog's transport, for who am I, the catalogs and the
// informers. Each request is bounded by ReadTimeout and classified with the provider's ReadFailures in the words of an
// annotation read or of a list. Holds no state, so concurrent reads are fine.
internal sealed class CatalogReader : IAnnotationPages
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
    public Task<StorytellerAnnotation> ReadAsync(FullKey key, bool includeDeleted, CancellationToken stopping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        var text = key.ToString();
        return RunAsync(
            text,
            text,
            $"Reading {text}",
            token => _transport.Client.GetAnnotationAsync(
                key.OrganizationName,
                key.ProjectName,
                key.ViewName,
                key.Annotation.ToString(),
                includeInactive: true,
                includeDeleted: includeDeleted ? true : null,
                token),
            annotation => AnnotationMapper.Map(key, annotation),
            DescribeRead,
            stopping,
            cancellationToken);
    }

    // One page of the active executions or usages under a responsibility, …/annotations/rst.{name}/{list}: the same
    // single partition as the list of executions, and readable by a machine restricted to the responsibility. Throws as
    // ReadAsync does; the exception's Key is the responsibility's.
    public Task<AnnotationPage> ReadPageAsync(FullKey responsibility, AnnotationList list, string? continuationToken, CancellationToken stopping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responsibility);

        var descendants = RouteOf(list);
        return ReadListPageAsync(
            responsibility,
            descendants,
            $"the {descendants} of {responsibility}",
            includeInactive: false,
            "so the informer needs one restricted to the responsibility, or an unrestricted one",
            continuationToken,
            stopping,
            cancellationToken);
    }

    // One page of the descendants of a type under any annotation, …/annotations/{key}/{segment}, active ones only
    // unless includeInactive, never deleted ones. Readable by a machine restricted to the ancestor or to an annotation
    // above it. Throws as ReadAsync does; the exception's Key is the ancestor's.
    public Task<AnnotationPage> ReadDescendantsPageAsync(FullKey ancestor, AnnotationType descendants, bool includeInactive, string? continuationToken, CancellationToken stopping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ancestor);

        return ReadListPageAsync(
            ancestor,
            AnnotationTypeFacts.Segment(descendants),
            $"the {AnnotationTypeFacts.Plural(descendants)} of {ancestor}",
            includeInactive,
            $"so listing them needs a machine restricted to {ancestor.Annotation} or to an annotation above it, or an unrestricted one",
            continuationToken,
            stopping,
            cancellationToken);
    }

    // In any status, deleted ones included, so a deleted annotation is told from a failure: null for one that is
    // missing or deleted. Throws as ReadAsync does otherwise.
    public async Task<StorytellerAnnotation?> FindAsync(FullKey key, CancellationToken cancellationToken)
    {
        try
        {
            var annotation = await ReadAsync(key, includeDeleted: true, CancellationToken.None, cancellationToken).ConfigureAwait(false);
            return annotation.DeletedAt is null ? annotation : null;
        }
        catch (StorytellerConfigurationException exception) when (IsMissing(exception))
        {
            return null;
        }
    }

    // The informers read until their own token ends; the container's stop disposes the transport.
    Task<AnnotationPage> IAnnotationPages.ReadPageAsync(FullKey responsibility, AnnotationList list, string? continuationToken, CancellationToken cancellationToken)
    {
        return ReadPageAsync(responsibility, list, continuationToken, CancellationToken.None, cancellationToken);
    }

    Task<AnnotationPage> IAnnotationPages.ReadDescendantsPageAsync(FullKey ancestor, AnnotationType descendants, bool includeInactive, string? continuationToken, CancellationToken cancellationToken)
    {
        return ReadDescendantsPageAsync(ancestor, descendants, includeInactive, continuationToken, CancellationToken.None, cancellationToken);
    }

    Task<StorytellerAnnotation> IAnnotationPages.ReadAnnotationAsync(FullKey key, bool includeDeleted, CancellationToken cancellationToken)
    {
        return ReadAsync(key, includeDeleted, CancellationToken.None, cancellationToken);
    }

    // The path segment of a list.
    private static string RouteOf(AnnotationList list)
    {
        return list switch
        {
            AnnotationList.Executions => AnnotationTypeFacts.Segment(AnnotationType.Execution),
            AnnotationList.Usages => AnnotationTypeFacts.Segment(AnnotationType.Usage),
            _ => throw new ArgumentOutOfRangeException(nameof(list), list, null),
        };
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
    private static string DescribeRead(string key, int status, string? errorCode)
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

    // The words of an answer to a list page; list: "the executions of {responsibility key}". restricted: what a machine
    // restricted to an annotation needs to list it, the end of the message of a 403 AnnotationRestricted.
    private static string DescribeList(string list, int status, string? errorCode, string restricted)
    {
        return status switch
        {
            403 when errorCode == AnnotationRestricted => $"The machine may not list {list}: a machine restricted to an annotation reads that annotation and its descendants only, {restricted}",
            400 => $"The platform does not accept the list of {list} or its continuation token",
            401 => $"The platform refused the credentials of the list of {list}; set the variables of a machine the project knows, or a credential in code",
            403 => $"The machine may not list {list}; it needs Annotation.Read on the project",
            404 => $"The platform does not know {list}; check the organization, project and view of the key, and that the endpoint is the Storyteller API base, /api included",
            >= 300 and < 400 => $"Listing {list} was redirected, which is not followed; set the endpoint to the final URL",
            _ when ReadFailures.IsSuccessStatus(status) => $"The answer to the list of {list} did not parse as a list of annotations",
            _ => $"Listing {list} failed",
        };
    }

    // descendants: the path segment. text: what the messages name, "the executions of {key}". restricted: the end of
    // the message of a 403 AnnotationRestricted.
    private Task<AnnotationPage> ReadListPageAsync(
        FullKey ancestor,
        string descendants,
        string text,
        bool includeInactive,
        string restricted,
        string? continuationToken,
        CancellationToken stopping,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            ancestor.ToString(),
            text,
            $"Listing {text}",
            token => _transport.Client.GetDescendantsAsync(
                ancestor.OrganizationName,
                ancestor.ProjectName,
                ancestor.ViewName,
                ancestor.Annotation.ToString(),
                descendants,
                continuationToken,
                includeInactive: includeInactive ? true : null,
                includeDeleted: null,
                token),
            response => new AnnotationPage(
                (response.Annotations ?? []).Where(item => item is not null).ToList().AsReadOnly(),
                string.IsNullOrEmpty(response.ContinuationToken) ? null : response.ContinuationToken),
            (list, status, errorCode) => DescribeList(list, status, errorCode, restricted),
            stopping,
            cancellationToken);
    }

    // key: the exception's Key. text: what the messages name. action: the read as a sentence starts it.
    private async Task<TResult> RunAsync<TAnswer, TResult>(
        string key,
        string text,
        string action,
        Func<CancellationToken, Task<TAnswer>> send,
        Func<TAnswer, TResult> map,
        Func<string, int, string?, string> describe,
        CancellationToken stopping,
        CancellationToken cancellationToken)
    {
        using var scope = new RunScope(_settings.ReadTimeout, stopping, cancellationToken);
        var read = send(scope.Token);

        try
        {
            // A handler that ignores cancellation cannot hold the read past ReadTimeout.
            return map(await read.WaitAsync(scope.Token).ConfigureAwait(false));
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
                $"{action} did not finish within the ReadTimeout of {_settings.ReadTimeout}.",
                key,
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
            throw ReadFailures.Classify(exception, text, _endpoint, describe).ToException(key);
        }
    }
}
