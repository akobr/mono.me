using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Cli.UnitTests.ErrorReporting;

// The local log as the file sink writes it: the rendered message, then the exception's ToString.
internal sealed class RecordingLogger : ILogger
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public string AllText => string.Join('\n', Entries.Select(entry => $"[{entry.Level}] {entry.Message}\n{entry.Exception}"));

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
