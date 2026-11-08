using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// What the descendant informer yields per poll: the changes, Removed first, then Changed, then Added, each by key.
// Failure: the poll could not tell what changed; the batch has no changes, and what the source holds stays.
internal sealed record DescendantBatch(
    IReadOnlyList<StorytellerAnnotationChange<StorytellerAnnotation>> Changes,
    StorytellerConfigurationException? Failure = null)
{
    public static DescendantBatch Failed(StorytellerConfigurationException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new DescendantBatch([], failure);
    }
}
