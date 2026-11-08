namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// What the informers and the catalog's lists share about their reads: a limit of reads at once, and why a confirmation
// read ends a held annotation.
internal static class PollReads
{
    // The read runs once the limiter lets it, and releases it when it ends.
    public static async Task<T> LimitAsync<T>(SemaphoreSlim limiter, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await read().ConfigureAwait(false);
        }
        finally
        {
            limiter.Release();
        }
    }

    // Why a confirmation read ends a held annotation; null while it stays. annotation: null for a read that answered 404.
    // includeInactive: disabled, scheduled and expired ones stay. A failure other than a 404 has no answer and is handled
    // before.
    public static StorytellerRemovalReason? ReasonOf(StorytellerAnnotation? annotation, bool includeInactive, DateTimeOffset now)
    {
        if (annotation is null)
        {
            return StorytellerRemovalReason.Missing;
        }

        if (annotation.DeletedAt is not null)
        {
            return StorytellerRemovalReason.Deleted;
        }

        if (includeInactive)
        {
            return null;
        }

        return annotation.GetStatus(now) switch
        {
            AnnotationStatus.Disabled => StorytellerRemovalReason.Disabled,
            AnnotationStatus.Scheduled => StorytellerRemovalReason.Scheduled,
            AnnotationStatus.Expired => StorytellerRemovalReason.Expired,
            _ => null,
        };
    }
}
