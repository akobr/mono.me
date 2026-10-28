namespace _42.Platform.Storyteller.Sdk.Hosting;

// The executions of the view of the options' Key, read through the platform's annotations. Needs Annotation.Read.
public interface ISubscriptionCatalog
{
    // One execution, in any status; null when it does not exist or is deleted. key: a full key, or an annotation key in
    // the organization, project and view of the options' Key; without that Key, an annotation key throws
    // StorytellerConfigurationException naming STORYTELLER_KEY.
    Task<StorytellerExecution?> GetExecutionAsync(string key, CancellationToken cancellationToken = default);

    // The informer: the subscriptions of a responsibility in a context, an active execution with its active usage per
    // subject, in the organization, project and view of the options' Key. Added for every subscription of the first
    // complete poll, then Added, Changed and Removed after every poll, Removed first, then Changed, then Added, each by
    // key. A failed poll emits nothing, logs a warning and polls again; with a zero interval the one poll's failure is
    // thrown. Each enumeration runs its own loop. pollInterval: 30 s when null, with a jitter of ±10 %, counted from
    // the end of a poll; zero polls once; otherwise 1 second to 45 days, as the subscriptions' PollInterval, or the call
    // throws ArgumentOutOfRangeException. Names are one key segment each: an empty one or one with a dot throws
    // ArgumentException, and without the options' Key the call throws StorytellerConfigurationException. Ends with
    // OperationCanceledException when the token is cancelled.
    IAsyncEnumerable<StorytellerExecutionChange> WatchExecutionsAsync(string responsibility, string context, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default);
}
