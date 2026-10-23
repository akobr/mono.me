using System.Text;

namespace _42.Platform.Storyteller.Sdk.Configuring;

// A snapshot of a provider's state, for health checks and diagnostics. Immutable; the refresher hands out a new one
// after every run.
public sealed record StorytellerConfigurationStatus
{
    // The full key of the applied document.
    public required string ActiveKey { get; init; }

    // The tag the next poll sends, if any.
    public string? ETag { get; init; }

    public StorytellerConfigurationOrigin Origin { get; init; }

    // When a document was last applied: swapped in and published to IConfiguration.
    public DateTimeOffset? LastAppliedAt { get; init; }

    // When the platform last answered a read with a 200 or a 304.
    public DateTimeOffset? LastSucceededAt { get; init; }

    public StorytellerConfigurationException? LastError { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }

    // Failed runs since the last successful one.
    public int ConsecutiveFailures { get; init; }

    public long TotalFailures { get; init; }

    // The origin is Cache or Empty, the last run failed, or the platform's document is one that did not bind.
    public bool IsDegraded { get; init; }

    // For ToString(), which health checks and logs print: the error by its message, which names keys, paths and
    // types only. The whole exception would add inner exceptions that the provider does not vouch for.
    private bool PrintMembers(StringBuilder builder)
    {
        builder
            .Append("ActiveKey = ").Append(ActiveKey)
            .Append(", ETag = ").Append(ETag)
            .Append(", Origin = ").Append(Origin)
            .Append(", LastAppliedAt = ").Append(LastAppliedAt)
            .Append(", LastSucceededAt = ").Append(LastSucceededAt)
            .Append(", LastError = ").Append(LastError?.Message)
            .Append(", LastErrorAt = ").Append(LastErrorAt)
            .Append(", ConsecutiveFailures = ").Append(ConsecutiveFailures)
            .Append(", TotalFailures = ").Append(TotalFailures)
            .Append(", IsDegraded = ").Append(IsDegraded);
        return true;
    }
}
