using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// The options of the subscriptions: builder.AddSubscriptions(context, options => …). Read and validated at
// registration; a later change of the object changes nothing.
public sealed class StorytellerSubscriptionOptions
{
    // How long after a poll ended the next one starts, with a jitter of ±10 %. Zero polls once; otherwise 1 s to 45 days.
    // Each poll lists the subscriptions and reads the document of every one of them again, so this is also how old a
    // document may get.
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    // The read of the documents; null: the mode of the options (STORYTELLER_MODE, Resolved when unset).
    public StorytellerReadMode? Mode { get; set; }

    // Reads of one poll at once, the lists, the annotations and the documents: 1 to 64.
    public int MaxConcurrentReads { get; set; } = 4;

    // The host's start waits until the subscriptions are ready (IStorytellerSubscriptions.WaitUntilReadyAsync), and
    // fails after StartTimeout.
    public bool WaitUntilReadyOnStart { get; set; }

    // Positive or infinite; used with WaitUntilReadyOnStart only.
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
