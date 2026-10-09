using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

/// <summary>
/// Records structured log entries at every level.
/// </summary>
internal sealed class CapturingLogger : ILogger<ConfigurationBindingResolver>
{
    private readonly LogLevel _minimum;

    public CapturingLogger(LogLevel minimum = LogLevel.Trace)
    {
        _minimum = minimum;
    }

    public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State)> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return new MemoryStream();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _minimum;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = new Dictionary<string, object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
            {
                values[key] = value;
            }
        }

        Entries.Add((logLevel, formatter(state, exception), values));
    }
}
