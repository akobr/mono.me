using System;

namespace _42.Platform.Storyteller.Sdk.Resilience;

// Retries, a circuit breaker and an attempt timeout for the Storyteller API and its token requests. Retry-After is
// always honoured.
public sealed class StorytellerResilienceOptions
{
    // Retries after the first attempt; 0 adds no retry, and a negative value is an error.
    public int MaxRetryAttempts { get; set; } = 3;

    // The first delay of an exponential backoff with jitter.
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    // Zero or infinite adds no attempt timeout; another negative value is an error.
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // false retries GET (and the other safe methods) only; the token client sets true, a token request has no side effect.
    public bool RetryUnsafeMethods { get; set; }

    public double FailureRatio { get; set; } = 0.5;

    // Calls, not attempts: the breaker sees the outcome of a call after its retries. Lower than the 100 of the standard
    // handler, which would never trip for a poller.
    public int MinimumThroughput { get; set; } = 3;

    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    // A copy for a client that must not see later changes of the caller's options.
    public StorytellerResilienceOptions Clone()
    {
        return (StorytellerResilienceOptions)MemberwiseClone();
    }
}
