using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// What a change source yields: the changes of subscriptions, Removed first, then Changed, then Added, each by key, and
// the full keys of the executions whose documents may have changed. Failure: the source could not tell what changed
// (a failed poll); the batch has no changes and no dirty keys, and what the source holds stays. A consumer that keeps
// a status, as the subscriptions do, learns of failed polls from it.
internal sealed record SubscriptionBatch(
    IReadOnlyList<StorytellerExecutionChange> Changes,
    IReadOnlyCollection<string> DirtyKeys,
    StorytellerConfigurationException? Failure = null)
{
    public static SubscriptionBatch Failed(StorytellerConfigurationException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new SubscriptionBatch([], [], failure);
    }
}
