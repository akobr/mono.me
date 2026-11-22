using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Keeps everything that was logged: the formatted message, the structured values, the exception, and the states of the
// scopes open at the time, outermost first.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<object> _scopes = [];

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        lock (_scopes)
        {
            _scopes.Add(state);
        }

        return new Scope(this, state);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToList() : [];
        object[] scopes;

        lock (_scopes)
        {
            scopes = [.. _scopes];
        }

        Entries.Enqueue(new Entry(logLevel, formatter(state, exception), values, exception, scopes));
    }

    internal sealed record Entry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Values,
        Exception? Exception,
        IReadOnlyList<object>? Scopes = null)
    {
        // The values of the open scopes that are key/value states, the inner ones winning.
        public IReadOnlyDictionary<string, object?> ScopeValues()
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var scope in Scopes ?? [])
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    foreach (var (key, value) in pairs)
                    {
                        values[key] = value;
                    }
                }
            }

            return values;
        }
    }

    private sealed class Scope(CapturingLogger<T> logger, object state) : IDisposable
    {
        public void Dispose()
        {
            lock (logger._scopes)
            {
                logger._scopes.Remove(state);
            }
        }
    }
}
