using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.UnitTests.Output;

// A console that records what is written to its output with the foreground color it had, as the tree renderer and the
// tags of sform write it (IConsole.ForegroundColor, then Write). Colors that equal the previous one are merged.
internal sealed class ColorRecordingConsole : IConsole
{
    private readonly RecordingWriter _out;

    public ColorRecordingConsole(Encoding? encoding = null)
    {
        _out = new RecordingWriter(this, encoding ?? new UTF8Encoding(false));
    }

    public event ConsoleCancelEventHandler? CancelKeyPress
    {
        add { }
        remove { }
    }

    public TextWriter Out => _out;

    public TextWriter Error { get; } = new StringWriter();

    public TextReader In { get; } = new StringReader(string.Empty);

    public bool IsInputRedirected => false;

    public bool IsOutputRedirected => false;

    public bool IsErrorRedirected => false;

    public ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;

    public ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;

    // Each run of text with its color.
    public IReadOnlyList<(string Text, ConsoleColor Color)> Segments => _out.Segments;

    public string Text => string.Concat(_out.Segments.Select(segment => segment.Text));

    public IReadOnlyList<string> Lines => Text.Split(Environment.NewLine);

    public void ResetColor()
    {
        ForegroundColor = ConsoleColor.Gray;
    }

    // The color of the first run that contains the text.
    public ConsoleColor? ColorOf(string text)
    {
        return _out.Segments.Where(segment => segment.Text.Contains(text, StringComparison.Ordinal)).Select(segment => (ConsoleColor?)segment.Color).FirstOrDefault();
    }

    private sealed class RecordingWriter : TextWriter
    {
        private readonly ColorRecordingConsole _console;
        private readonly List<(string Text, ConsoleColor Color)> _segments = [];

        public RecordingWriter(ColorRecordingConsole console, Encoding encoding)
        {
            _console = console;
            Encoding = encoding;
        }

        public override Encoding Encoding { get; }

        public IReadOnlyList<(string Text, ConsoleColor Color)> Segments => _segments;

        public override void Write(char value)
        {
            Write(value.ToString());
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var color = _console.ForegroundColor;

            if (_segments.Count > 0 && _segments[^1].Color == color)
            {
                _segments[^1] = (_segments[^1].Text + value, color);
                return;
            }

            _segments.Add((value, color));
        }
    }
}
