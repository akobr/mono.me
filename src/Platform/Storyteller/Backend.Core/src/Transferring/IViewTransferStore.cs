namespace _42.Platform.Storyteller.Transferring;

public interface IViewTransferStore
{
    // Stores a new job; the returned record carries the ETag.
    Task<ViewTransferRecord> CreateAsync(ViewTransferRecord record, CancellationToken cancellationToken = default);

    // A job is only found through the view it was started in.
    Task<ViewTransferRecord?> GetAsync(
        string organization,
        string project,
        string view,
        ViewTransferKind kind,
        string id,
        CancellationToken cancellationToken = default);

    // Replaces the job when its ETag still matches; null when another writer changed or removed it.
    Task<ViewTransferRecord?> TryReplaceAsync(ViewTransferRecord record, CancellationToken cancellationToken = default);

    // Queued jobs changed since queuedSince and running jobs changed since runningSince, of the project in every view.
    Task<int> CountActiveAsync(
        string organization,
        string project,
        DateTimeOffset queuedSince,
        DateTimeOffset runningSince,
        CancellationToken cancellationToken = default);
}
