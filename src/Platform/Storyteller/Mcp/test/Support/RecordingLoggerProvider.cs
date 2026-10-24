using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// Keeps every formatted message and exception text, so a test can prove a value never reached a log.
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            entries.Enqueue($"{category} scope: {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
