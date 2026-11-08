namespace _42.Platform.Storyteller.Sdk.Hosting;

// What happened between two polls of an informer: to a subscription (WatchExecutionsAsync, the subscriptions), or to an
// annotation of a target (IStorytellerCatalog.WatchAsync).
public enum StorytellerChangeKind
{
    // A subscription the informer did not hold: its execution and its usage are active. Or an annotation of the target
    // it did not hold.
    Added = 0,

    // The execution or the usage of a held subscription changed, and both are still active. Or a held annotation of the
    // target changed, and it is still one.
    Changed,

    // A held subscription ended: its execution or its usage is deleted, inactive or gone. Or a held annotation of the
    // target is deleted, gone, or inactive while the target takes active ones only.
    Removed,
}
