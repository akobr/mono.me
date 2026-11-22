using _42.Platform.Storyteller.Notifying;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

// Runs what a test gives it and keeps what it was called with.
internal sealed class RecordingActionHandler(string action) : IViewInboxActionHandler
{
    public string Action { get; } = action;

    public List<(ViewInboxActionContext Context, CancellationToken CancellationToken)> Runs { get; } = [];

    public Func<ViewInboxActionContext, ViewInboxActionResult> Run { get; set; } = _ => ViewInboxActionResult.None;

    public Task<ViewInboxActionResult> RunAsync(ViewInboxActionContext context, CancellationToken cancellationToken)
    {
        Runs.Add((context, cancellationToken));

        try
        {
            return Task.FromResult(Run(context));
        }
        catch (Exception exception)
        {
            // as a handler that fails in the middle of its work: the failure comes with the task
            return Task.FromException<ViewInboxActionResult>(exception);
        }
    }
}
