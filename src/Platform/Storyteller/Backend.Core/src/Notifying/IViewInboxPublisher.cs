namespace _42.Platform.Storyteller.Notifying;

// For producers of the back end.
public interface IViewInboxPublisher
{
    // With a Key the push is idempotent: the same key and time give the same item.
    Task<ViewInboxItem> PushAsync(ViewInboxPush push, CancellationToken cancellationToken = default);
}
