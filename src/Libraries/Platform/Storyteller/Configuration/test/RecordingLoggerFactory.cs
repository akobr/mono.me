using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// Keeps every entry with its formatted message, so tests can check what is logged and that no secret is.
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(this, categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
    }

    internal sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class Logger : ILogger
    {
        private readonly RecordingLoggerFactory _owner;
        private readonly string _category;

        public Logger(RecordingLoggerFactory owner, string category)
        {
            _owner = owner;
            _category = category;
        }

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
            lock (_owner._lock)
            {
                _owner._entries.Add(new LogEntry(_category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
