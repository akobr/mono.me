namespace _42.Platform.Storyteller.Notifying;

// Runs what the acceptance of an action starts, typically a background job whose result arrives as a notification.
// Register it as a singleton: the inbox service is one and takes the handlers when it is built, so a scoped handler
// would live as long as the host. Two handlers of one code stop the inbox service when it is built (the first call of a
// route), never the jobs.
public interface IViewInboxActionHandler
{
    // The code of the actions it runs (ViewInboxItem.Action).
    string Action { get; }

    // The item is already accepted, and the token is never the caller's: the work runs to its end. Throws only when
    // it started nothing, because the item is then open again and can be accepted once more.
    Task<ViewInboxActionResult> RunAsync(ViewInboxActionContext context, CancellationToken cancellationToken);
}
