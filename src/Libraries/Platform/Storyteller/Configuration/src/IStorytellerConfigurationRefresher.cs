namespace _42.Platform.Storyteller.Sdk.Configuring;

// Controls one Storyteller source; the same instance however often its builder builds.
public interface IStorytellerConfigurationRefresher
{
    // The full key of the current snapshot.
    string ActiveKey { get; }

    StorytellerConfigurationStatus Status { get; }

    // Reads the configuration now and returns whether the run succeeded; never throws for a failed read. Concurrent
    // calls share a run that has not started yet, so every call sees a read made after it. The task completes once
    // IConfiguration and IOptionsMonitor show the result; called from a change callback, it completes at the swap
    // instead, and the document is notified after the current notification.
    Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default);

    // Applies another configuration of the same organization and project: a full key, or an annotation key in the active
    // organization, project and view. Throws StorytellerConfigurationException on failure and keeps the previous key and
    // document. Called from a change callback, it completes at the swap, as TryRefreshAsync does; a document that then
    // does not bind is rolled back and shows only in Status.
    Task SwitchKeyAsync(string key, CancellationToken cancellationToken = default);
}
