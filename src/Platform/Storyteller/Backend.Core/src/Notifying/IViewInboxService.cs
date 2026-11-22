namespace _42.Platform.Storyteller.Notifying;

// For the routes. The role is the caller's role on the project: an item above it is not found, whatever is asked of it.
public interface IViewInboxService
{
    Task<ViewInboxResponse> GetItemsAsync(
        string organization,
        string project,
        string view,
        ViewInboxQuery query,
        CancellationToken cancellationToken = default);

    // Only an open Action: ConflictException with InboxItemNotAction or InboxItemClosed, FeatureNotConfiguredException
    // (InboxActionNotAvailable) for an action without a handler, or what the handler throws.
    Task<ViewInboxItem> AcceptAsync(
        string organization,
        string project,
        string view,
        string id,
        AccountRole role,
        AuditActor actor,
        CancellationToken cancellationToken = default);

    // Every kind, while open: ConflictException with InboxItemClosed afterwards.
    Task<ViewInboxItem> DismissAsync(
        string organization,
        string project,
        string view,
        string id,
        AccountRole role,
        AuditActor actor,
        CancellationToken cancellationToken = default);
}
