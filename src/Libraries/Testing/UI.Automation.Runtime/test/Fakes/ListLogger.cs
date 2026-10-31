using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// Every message, with the exception's text when there is one, as a logger would write it.
internal sealed class ListLogger : ILogger
{
    private readonly object gate = new();
    private readonly List<(LogLevel Level, string Text)> entries = [];

    public IReadOnlyList<(LogLevel Level, string Text)> Entries
    {
        get
        {
            lock (gate)
            {
                return [.. entries];
            }
        }
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
        var text = formatter(state, exception);
        lock (gate)
        {
            entries.Add((logLevel, exception is null ? text : $"{text} {exception}"));
        }
    }
}
