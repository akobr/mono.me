namespace _42.Platform.Storyteller.Notifying;

public sealed record ViewInboxActionResult
{
    public static ViewInboxActionResult None { get; } = new();

    // The job the acceptance started.
    public ViewInboxJob? Job { get; init; }
}
