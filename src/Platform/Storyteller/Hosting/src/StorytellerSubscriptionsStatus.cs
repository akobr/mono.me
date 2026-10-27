using System.Text;
using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A snapshot of the subscriptions, for health checks and diagnostics, like the provider's
// StorytellerConfigurationStatus. Immutable; the subscriptions hand out a new one after every poll.
public sealed record StorytellerSubscriptionsStatus
{
    // The first poll completed, and every subscription in it had its first document read.
    public bool IsReady { get; init; }

    public DateTimeOffset? LastPollSucceededAt { get; init; }

    // The failure of the latest poll; null when it succeeded.
    public StorytellerConfigurationException? LastPollError { get; init; }

    // Failed polls since the last one that succeeded.
    public int ConsecutivePollFailures { get; init; }

    // The subscriptions that have a good document, as GetAll() lists them.
    public int Subscriptions { get; init; }

    // The subscriptions without a first good document, by key.
    public IReadOnlyList<StorytellerPendingSubscription> Pending { get; init; } = [];

    // The subscriptions whose latest read failed, while they keep their last good document.
    public int Failing { get; init; }

    // The latest poll failed, or a subscription is pending or failing.
    public bool IsDegraded => ConsecutivePollFailures > 0 || Pending.Count > 0 || Failing > 0;

    // For ToString(), which health checks and logs print: errors by their messages, which name keys and statuses only.
    // The whole exception would add inner exceptions that Hosting does not vouch for.
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("IsReady = ").Append(IsReady)
            .Append(", LastPollSucceededAt = ").Append(LastPollSucceededAt)
            .Append(", LastPollError = ").Append(LastPollError?.Message)
            .Append(", ConsecutivePollFailures = ").Append(ConsecutivePollFailures)
            .Append(", Subscriptions = ").Append(Subscriptions)
            .Append(", Pending = [").AppendJoin(", ", Pending.Select(pending => pending.Key)).Append(']')
            .Append(", Failing = ").Append(Failing)
            .Append(", IsDegraded = ").Append(IsDegraded);
        return true;
    }
}
