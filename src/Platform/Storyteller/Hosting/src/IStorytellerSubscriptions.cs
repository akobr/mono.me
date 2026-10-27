namespace _42.Platform.Storyteller.Sdk.Hosting;

// The subscriptions of one responsibility in one context, with the resolved document of each, kept in memory by a hosted
// informer: builder.AddSubscriptions(…). A subscription is an active execution exe.{subject}.{responsibility}.{context}
// with its active usage usg.{subject}.{responsibility}. Needs Annotation.Read and Configuration.Read (Secrets.Read for
// secrets), on an unrestricted machine or one restricted to the responsibility.
public interface IStorytellerSubscriptions
{
    string Responsibility { get; }

    string Context { get; }

    // A snapshot for health checks and diagnostics; a new one after every poll.
    StorytellerSubscriptionsStatus Status { get; }

    // Completes when the first poll completed and every subscription in it had its first document read, whether the
    // reads succeeded or not, so one broken tenant does not block a worker. Throws the failure of the latest poll while
    // the subscriptions were never ready and that failure is definitive (400, 401, 403, 404), so a misconfigured worker
    // does not wait for ever; the loop keeps polling. Throws OperationCanceledException when the token is cancelled or
    // the informer stopped first, and the failure of the one poll of a zero PollInterval.
    Task WaitUntilReadyAsync(CancellationToken cancellationToken = default);

    // The subscriptions that have a good document, by subject, ordinally. A pending one, whose first reads failed, is
    // not among them.
    IReadOnlyList<StorytellerSubscription> GetAll();

    // By subject name, ordinally; null when it is not subscribed, not ready yet or pending.
    StorytellerSubscription? Find(string subject);

    // Added for every current subscription first, then the changes as they happen: Removed, Changed and Added after
    // each poll, each by key, every one with its document. Several watchers may run at once. Ends when the informer
    // stops, with OperationCanceledException when the token is cancelled, and with the informer's
    // StorytellerConfigurationException when it broke, which it logs as an error.
    IAsyncEnumerable<StorytellerSubscriptionChange> WatchAsync(CancellationToken cancellationToken = default);
}
