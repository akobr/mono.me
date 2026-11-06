using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleExecutor.UnitTests;

internal sealed class FakeConsole : IConsole
{
#pragma warning disable CS0067 // Nothing presses Ctrl+C in a test.
    public event ConsoleCancelEventHandler? CancelKeyPress;
#pragma warning restore CS0067

    public StringWriter OutWriter { get; } = new();

    public StringWriter ErrorWriter { get; } = new();

    public TextWriter Out => OutWriter;

    public TextWriter Error => ErrorWriter;

    public TextReader In => TextReader.Null;

    public bool IsInputRedirected => true;

    public bool IsOutputRedirected => true;

    public bool IsErrorRedirected => true;

    public ConsoleColor ForegroundColor { get; set; }

    public ConsoleColor BackgroundColor { get; set; }

    public void ResetColor()
    {
    }
}
