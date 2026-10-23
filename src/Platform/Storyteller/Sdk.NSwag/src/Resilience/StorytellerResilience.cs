using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace _42.Platform.Storyteller.Sdk.Resilience;

public static class StorytellerResilience
{
    // The circuit breaker sits outside the retries, so it counts one outcome per call: with the defaults, the attempts of
    // one failing call would open it on their own and block every other request of the client. The attempt timeout is
    // innermost, as in the standard handler. The pipeline keeps the state of its breaker, so one pipeline belongs to one
    // client. Zero turns the retries or the attempt timeout off; a negative value is a mistake, not a way to say so.
    public static ResiliencePipeline<HttpResponseMessage> CreatePipeline(StorytellerResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxRetryAttempts, "options.MaxRetryAttempts");

        if (options.AttemptTimeout < TimeSpan.Zero && options.AttemptTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                "options.AttemptTimeout",
                options.AttemptTimeout,
                "The attempt timeout must be positive, zero or Timeout.InfiniteTimeSpan; zero and infinite add none.");
        }

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = options.FailureRatio,
            MinimumThroughput = options.MinimumThroughput,
            SamplingDuration = options.SamplingDuration,
            BreakDuration = options.BreakDuration,
            ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome, args.Context.CancellationToken.IsCancellationRequested)),
        });

        if (options.MaxRetryAttempts > 0)
        {
            var retry = new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.BaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldRetryAfterHeader = true,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome, args.Context.CancellationToken.IsCancellationRequested)),
            };

            // After ShouldHandle, which it wraps.
            if (!options.RetryUnsafeMethods)
            {
                retry.DisableForUnsafeHttpMethods();
            }

            builder.AddRetry(retry);
        }

        if (options.AttemptTimeout > TimeSpan.Zero && options.AttemptTimeout != Timeout.InfiniteTimeSpan)
        {
            builder.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = options.AttemptTimeout });
        }

        return builder.Build();
    }

    // The default predicate without 501 and 505: a retry gets the same answer, and one key that the server cannot serve
    // must not open the circuit for the others. The default's overload that also takes a connect timeout of the handler
    // (a cancellation the caller did not request) is experimental, so that check is repeated here.
    private static bool IsTransient(Outcome<HttpResponseMessage> outcome, bool isCancelledByCaller)
    {
        var isConnectTimeout = outcome.Exception is OperationCanceledException { InnerException: TimeoutException } && !isCancelledByCaller;

        return (isConnectTimeout || HttpClientResiliencePredicates.IsTransient(outcome))
            && outcome.Result?.StatusCode is not (HttpStatusCode.NotImplemented or HttpStatusCode.HttpVersionNotSupported);
    }
}
