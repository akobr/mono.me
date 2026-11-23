namespace _42.Platform.Storyteller.Notifying;

// Runs what the acceptance of an action starts, typically a background job whose result arrives as a notification.
public interface IViewInboxActionHandler
{
    // The code of the actions it runs (ViewInboxItem.Action).
    string Action { get; }

    // The item is already accepted, and the token is never the caller's: the work runs to its end. Throws only when
    // it started nothing, because the item is then open again and can be accepted once more.
    Task<ViewInboxActionResult> RunAsync(ViewInboxActionContext context, CancellationToken cancellationToken);
}
