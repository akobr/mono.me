namespace _42.Platform.Storyteller.Sdk.Hosting;

// Who am I: the annotation of the options' Key (STORYTELLER_KEY) and its ancestors, for a process that knows its key.
// Follows the options' key; the provider refresher's SwitchKeyAsync does not move it. Needs Annotation.Read.
public interface IExecutionContext
{
    // The first call reads and waits; later calls return the cached snapshot at once and refresh it in the background
    // when it is older than the refresh interval. The token ends the caller's wait, not a read other callers share.
    Task<StorytellerExecutionContext> GetAsync(CancellationToken cancellationToken = default);

    // Reads now, ancestors that were unreadable included; throws on failure and keeps the previous snapshot.
    Task<StorytellerExecutionContext> RefreshAsync(CancellationToken cancellationToken = default);
}
