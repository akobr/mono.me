using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Keeps everything that was logged: the formatted message, the structured values and the exception.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<Entry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? pairs.ToList() : [];
        Entries.Enqueue(new Entry(logLevel, formatter(state, exception), values, exception));
    }

    internal sealed record Entry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values, Exception? Exception);
}
