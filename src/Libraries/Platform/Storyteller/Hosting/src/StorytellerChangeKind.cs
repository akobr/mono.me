namespace _42.Platform.Storyteller.Sdk.Hosting;

// What happened to a subscription between two polls of the informer.
public enum StorytellerChangeKind
{
    // A subscription the informer did not hold: its execution and its usage are active.
    Added = 0,

    // The execution or the usage of a held subscription changed, and both are still active.
    Changed,

    // A held subscription ended: its execution or its usage is deleted, inactive or gone.
    Removed,
}
