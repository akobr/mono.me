namespace _42.Platform.Storyteller.Notifying;

// The time to live of a write is the time the item still has; the store removes the item after it.
public interface IViewInboxStore
{
    // Null when an item with the id exists already.
    Task<ViewInboxRecord?> TryCreateAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    // An item is only found through the view it was pushed to.
    Task<ViewInboxRecord?> GetAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);

    // Null when the ETag no longer matches or the item is gone.
    Task<ViewInboxRecord?> TryReplaceAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    // Newest first: at most take items below the item id before, of the roles and the filter; without ETags.
    Task<IReadOnlyList<ViewInboxRecord>> QueryAsync(
        string organization,
        string project,
        string view,
        ViewInboxFilter filter,
        IReadOnlyCollection<AccountRole> roles,
        string? before,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> CountOpenAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AccountRole> roles,
        CancellationToken cancellationToken = default);
}
