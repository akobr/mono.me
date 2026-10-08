using System;
using System.Collections.Generic;
using System.Linq;
using _42.CLI.Toolkit.Output;
using Alba.CsConsoleFormat;
using Moq;

namespace _42.Platform.Cli.UnitTests.Commands;

// Records the plain text of every WriteLine call; themed spans are reduced to their text.
internal sealed class RecordingConsole
{
    private readonly List<string> _lines = [];

    public RecordingConsole()
    {
        Mock.SetupGet(console => console.Theme).Returns(new ConsoleTheme
        {
            ForegroundColor = ConsoleColor.Gray,
            HeaderColor = ConsoleColor.White,
            HighlightColor = ConsoleColor.Magenta,
            LowlightColor = ConsoleColor.DarkGray,
            ErrorColor = ConsoleColor.Red,
        });
        Mock.Setup(console => console.WriteLine(It.IsAny<object[]>()))
            .Callback<object[]>(elements => _lines.Add(string.Concat(elements.Select(ToText))));
    }

    public Mock<IExtendedConsole> Mock { get; } = new();

    public IExtendedConsole Object => Mock.Object;

    public IReadOnlyList<string> Lines => _lines;

    private static string ToText(object element)
    {
        return element is Span span ? span.Text : element.ToString() ?? string.Empty;
    }
}
