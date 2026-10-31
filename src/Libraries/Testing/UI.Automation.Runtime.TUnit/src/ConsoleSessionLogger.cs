using System.Text;
using Microsoft.Extensions.Logging;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The session's log on the terminal. TUnit routes the console output of hooks into the session's own output, which the
// terminal shows only with --output Detailed, so this writes to the standard output stream of the process directly
// (but not under --server, where an IDE drives the process and the console stays TUnit's). Under --list-tests it writes
// to the standard error stream, so the output of '--list-tests json' stays a JSON document. Information and above; a
// warning shows the exception's message, an error the whole exception. Once the settings load, every line goes through
// Redact: their secrets are *** (a Playwright call log in an exception may hold a typed password).
internal sealed class ConsoleSessionLogger : ILogger
{
    public const string Prefix = "ui-automation";

    private readonly object gate = new();
    private readonly TextWriter? terminal;

    private ConsoleSessionLogger()
    {
        var arguments = Environment.GetCommandLineArgs();
        if (!arguments.Contains("--server", StringComparer.Ordinal))
        {
            var encoding = Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : Console.OutputEncoding;
            var stream = arguments.Contains("--list-tests", StringComparer.Ordinal) ? Console.OpenStandardError() : Console.OpenStandardOutput();
            terminal = new StreamWriter(stream, encoding) { AutoFlush = true };
        }
    }

    public static ConsoleSessionLogger Instance { get; } = new();

    // SettingsRedaction.CreateRedactor of the loaded settings; null before they load.
    public Func<string?, string>? Redact { get; set; }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel is >= LogLevel.Information and < LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        Write(Format(logLevel, formatter(state, exception), exception));
    }

    // Every line of text, prefixed, with the secrets of the settings as ***.
    public void Write(string text)
    {
        var lines = Lines(text);
        lock (gate)
        {
            var writer = terminal ?? Console.Out;
            foreach (var line in lines)
            {
                writer.WriteLine($"{Prefix}: {line}");
            }
        }
    }

    // A warning shows the exception's message, an error the whole exception.
    internal static string Format(LogLevel logLevel, string message, Exception? exception)
    {
        return logLevel switch
        {
            LogLevel.Warning => exception is null ? $"warning: {message}" : $"warning: {message} ({exception.Message})",
            >= LogLevel.Error => exception is null ? $"error: {message}" : $"error: {message}{Environment.NewLine}{exception}",
            _ => message,
        };
    }

    // The lines Write prints, before their prefix.
    internal string[] Lines(string text)
    {
        return (Redact?.Invoke(text) ?? text).ReplaceLineEndings("\n").Split('\n');
    }
}
