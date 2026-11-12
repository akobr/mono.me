using System.Text;

using Sentry;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// Keeps a storm (a Cosmos DB outage turns every request into an error, on up to 10 instances) from using up the
// organization's monthly Sentry quota, which the admin UI shares. Per instance:
// - the same error is sent once per RepeatInterval; its repeats are counted, and the next event of the error carries
//   the count in the context "throttle";
// - at most MaxEventsPerHour per clock hour, which an error the instance has not sent before passes (a root cause isn't
//   hidden behind its symptoms), and MaxEventsPerDay per UTC day, which nothing passes.
// The first step of BeforeSend, called concurrently: one lock guards the state.
internal sealed class SentryEventThrottle
{
    public const string ContextName = "throttle";
    public const int MaxSignatures = 1_000;

    private readonly object _lock = new();
    private readonly Dictionary<string, Signature> _signatures = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly TimeSpan _repeatInterval;
    private readonly int _maxPerHour;
    private readonly int _maxPerDay;

    private DateTimeOffset _hour;
    private int _hourCount;
    private DateOnly _day;
    private int _dayCount;

    public SentryEventThrottle(ErrorReportingOptions options, TimeProvider time)
    {
        _time = time;
        _repeatInterval = options.RepeatInterval;
        _maxPerHour = options.MaxEventsPerHour;
        _maxPerDay = options.MaxEventsPerDay;
    }

    public bool ShouldSend(SentryEvent sentryEvent)
    {
        var key = GetSignature(sentryEvent);
        int suppressed;

        lock (_lock)
        {
            var now = _time.GetUtcNow();
            Roll(now);

            _signatures.TryGetValue(key, out var signature);

            if (signature is not null
                && _repeatInterval > TimeSpan.Zero
                && now - signature.LastSent < _repeatInterval)
            {
                signature.Suppressed++;
                return false;
            }

            if ((_maxPerDay > 0 && _dayCount >= _maxPerDay)
                || (signature is not null && _maxPerHour > 0 && _hourCount >= _maxPerHour))
            {
                if (signature is not null)
                {
                    signature.Suppressed++;
                }

                return false;
            }

            _hourCount++;
            _dayCount++;

            if (signature is not null)
            {
                suppressed = signature.Suppressed;
                signature.LastSent = now;
                signature.Suppressed = 0;
            }
            else
            {
                suppressed = 0;
                Prune(now);
                _signatures[key] = new Signature(now);
            }
        }

        if (suppressed > 0)
        {
            sentryEvent.Contexts[ContextName] = new Dictionary<string, object> { ["suppressed"] = suppressed };
        }

        return true;
    }

    // The same error: the route (transaction), the logger, the message template, the exception types and the method that
    // threw the innermost exception, so two defects on one route are two errors. No text of the message: it varies.
    internal static string GetSignature(SentryEvent sentryEvent)
    {
        var signature = new StringBuilder()
            .Append(sentryEvent.TransactionName).Append('|')
            .Append(sentryEvent.Logger).Append('|')
            .Append(sentryEvent.Message?.Message);

        for (var exception = sentryEvent.Exception; exception is not null; exception = exception.InnerException)
        {
            signature.Append('|').Append(exception.GetType().FullName);
        }

        if (sentryEvent.Exception?.GetBaseException().TargetSite is { } site)
        {
            signature.Append('@').Append(site.DeclaringType?.FullName).Append('.').Append(site.Name);
        }

        return signature.ToString();
    }

    private void Roll(DateTimeOffset now)
    {
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);

        if (hour != _hour)
        {
            _hour = hour;
            _hourCount = 0;
        }

        var day = DateOnly.FromDateTime(now.UtcDateTime);

        if (day != _day)
        {
            _day = day;
            _dayCount = 0;
        }
    }

    // Bounded: the errors outside their repeat interval go first, then the oldest half.
    private void Prune(DateTimeOffset now)
    {
        if (_signatures.Count < MaxSignatures)
        {
            return;
        }

        foreach (var expired in _signatures.Where(pair => now - pair.Value.LastSent >= _repeatInterval).Select(pair => pair.Key).ToList())
        {
            _signatures.Remove(expired);
        }

        if (_signatures.Count < MaxSignatures)
        {
            return;
        }

        foreach (var oldest in _signatures.OrderBy(pair => pair.Value.LastSent).Take(_signatures.Count / 2).Select(pair => pair.Key).ToList())
        {
            _signatures.Remove(oldest);
        }
    }

    private sealed class Signature
    {
        public Signature(DateTimeOffset lastSent)
        {
            LastSent = lastSent;
        }

        public DateTimeOffset LastSent { get; set; }

        public int Suppressed { get; set; }
    }
}
