using _42.Platform.Storyteller.Api.ErrorReporting;

using Sentry;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The volume control of BeforeSend: a storm must not use up the organization's monthly quota.
public class SentryEventThrottleTests
{
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void ShouldSend_ARepeat_IsDroppedUntilTheIntervalPassesAndThenCounted()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromHours(1));

        throttle.ShouldSend(Event(Throw(First))).ShouldBeTrue();
        throttle.ShouldSend(Event(Throw(First))).ShouldBeFalse();
        throttle.ShouldSend(Event(Throw(First))).ShouldBeFalse();

        _time.Now += TimeSpan.FromHours(1);
        var next = Event(Throw(First));
        throttle.ShouldSend(next).ShouldBeTrue();

        var context = next.Contexts[SentryEventThrottle.ContextName].ShouldBeOfType<Dictionary<string, object>>();
        context["suppressed"].ShouldBe(2);
    }

    [Fact]
    public void ShouldSend_AnotherThrowSite_IsAnotherError()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromHours(1));

        throttle.ShouldSend(Event(Throw(First))).ShouldBeTrue();
        throttle.ShouldSend(Event(Throw(Second))).ShouldBeTrue();
    }

    [Fact]
    public void ShouldSend_AnotherRoute_IsAnotherError()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromHours(1));

        throttle.ShouldSend(Event(Throw(First), "GET /api/v1/{organization}")).ShouldBeTrue();
        throttle.ShouldSend(Event(Throw(First), "PUT /api/v1/{organization}")).ShouldBeTrue();
    }

    [Fact]
    public void ShouldSend_TheHourlyCap_StopsRepeatsButNotNewErrors()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromMinutes(1), maxPerHour: 2);

        throttle.ShouldSend(Event(Throw(First), "a")).ShouldBeTrue();
        throttle.ShouldSend(Event(Throw(First), "b")).ShouldBeTrue();

        // the cap is reached: a repeat after its interval waits for the next hour, a new error passes
        _time.Now += TimeSpan.FromMinutes(2);
        throttle.ShouldSend(Event(Throw(First), "a")).ShouldBeFalse();
        throttle.ShouldSend(Event(Throw(First), "c")).ShouldBeTrue();

        _time.Now += TimeSpan.FromHours(1);
        var next = Event(Throw(First), "a");
        throttle.ShouldSend(next).ShouldBeTrue();

        // the repeat the cap dropped is counted too
        var context = next.Contexts[SentryEventThrottle.ContextName].ShouldBeOfType<Dictionary<string, object>>();
        context["suppressed"].ShouldBe(1);
    }

    [Fact]
    public void ShouldSend_TheDailyCap_StopsEverythingUntilTheNextDay()
    {
        var throttle = Create(repeatInterval: TimeSpan.Zero, maxPerHour: 0, maxPerDay: 3);

        for (var index = 0; index < 3; index++)
        {
            throttle.ShouldSend(Event(Throw(First), $"route {index}")).ShouldBeTrue();
        }

        throttle.ShouldSend(Event(Throw(First), "a new route")).ShouldBeFalse();

        _time.Now = new DateTimeOffset(_time.Now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        throttle.ShouldSend(Event(Throw(First), "a new route")).ShouldBeTrue();
    }

    [Fact]
    public void ShouldSend_ZeroLimits_SendEverything()
    {
        var throttle = Create(repeatInterval: TimeSpan.Zero, maxPerHour: 0, maxPerDay: 0);

        for (var index = 0; index < 100; index++)
        {
            throttle.ShouldSend(Event(Throw(First))).ShouldBeTrue();
        }
    }

    [Fact]
    public void ShouldSend_ConcurrentCalls_KeepTheDailyCap()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromHours(1), maxPerHour: 10, maxPerDay: 40);
        var exception = Throw(First);
        var sent = 0;

        Parallel.For(0, 2_000, index =>
        {
            if (throttle.ShouldSend(Event(exception, $"route {index % 500}")))
            {
                Interlocked.Increment(ref sent);
            }
        });

        sent.ShouldBe(40);
    }

    [Fact]
    public void ShouldSend_ManyErrors_StayBounded()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromMinutes(1), maxPerHour: 0, maxPerDay: 0);

        for (var index = 0; index < SentryEventThrottle.MaxSignatures * 3; index++)
        {
            _time.Now += TimeSpan.FromSeconds(1);
            throttle.ShouldSend(Event(Throw(First), $"route {index}")).ShouldBeTrue();
            throttle.Count.ShouldBeLessThanOrEqualTo(SentryEventThrottle.MaxSignatures);
        }

        // the latest error is still known
        throttle.ShouldSend(Event(Throw(First), $"route {(SentryEventThrottle.MaxSignatures * 3) - 1}")).ShouldBeFalse();
    }

    [Fact]
    public void ShouldSend_ManyRecentErrors_ForgetTheOldestHalf()
    {
        var throttle = Create(repeatInterval: TimeSpan.FromHours(1));

        // one more than it remembers, all inside the interval: only the oldest half can go
        for (var index = 0; index <= SentryEventThrottle.MaxSignatures; index++)
        {
            _time.Now += TimeSpan.FromSeconds(1);
            throttle.ShouldSend(Event(Throw(First), $"route {index}")).ShouldBeTrue();
        }

        throttle.Count.ShouldBe((SentryEventThrottle.MaxSignatures / 2) + 1);
        throttle.ShouldSend(Event(Throw(First), $"route {SentryEventThrottle.MaxSignatures - 1}")).ShouldBeFalse();
        throttle.ShouldSend(Event(Throw(First), "route 0")).ShouldBeTrue();
    }

    [Fact]
    public void GetSignature_AnotherLoggerOrTemplate_IsAnotherError()
    {
        var exception = Throw(First);
        var signature = SentryEventThrottle.GetSignature(Event(exception));

        var otherLogger = Event(exception);
        otherLogger.Logger = "_42.Platform.Storyteller.Api.V1.KeyValuesHttp";
        var otherTemplate = Event(exception);
        otherTemplate.Message = new SentryMessage { Message = "Another failure" };

        SentryEventThrottle.GetSignature(otherLogger).ShouldNotBe(signature);
        SentryEventThrottle.GetSignature(otherTemplate).ShouldNotBe(signature);
    }

    [Fact]
    public void GetSignature_LeavesTheMessageTextOut()
    {
        var one = Event(Throw(First));
        one.Message = new SentryMessage { Message = "Writing {Key} failed", Formatted = "Writing a failed" };
        var other = Event(Throw(First));
        other.Message = new SentryMessage { Message = "Writing {Key} failed", Formatted = "Writing b failed" };

        SentryEventThrottle.GetSignature(one).ShouldBe(SentryEventThrottle.GetSignature(other));
    }

    private static void First()
    {
        throw new InvalidOperationException("first");
    }

    private static void Second()
    {
        throw new InvalidOperationException("second");
    }

    private static Exception Throw(Action thrower)
    {
        try
        {
            thrower();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    private static SentryEvent Event(Exception exception, string transaction = "PUT /api/v1/{organization}")
    {
        return new SentryEvent(exception)
        {
            TransactionName = transaction,
            Logger = "_42.Platform.Storyteller.Api.ErrorHandling.ExceptionHandlingMiddleware",
            Message = new SentryMessage { Message = "Error processing invocation" },
        };
    }

    private SentryEventThrottle Create(TimeSpan repeatInterval, int maxPerHour = 0, int maxPerDay = 0)
    {
        return new SentryEventThrottle(
            new ErrorReportingOptions { RepeatInterval = repeatInterval, MaxEventsPerHour = maxPerHour, MaxEventsPerDay = maxPerDay },
            _time);
    }
}
