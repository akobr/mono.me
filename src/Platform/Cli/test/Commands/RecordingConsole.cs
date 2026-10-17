using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using _42.CLI.Toolkit.Output;
using Alba.CsConsoleFormat;
using Moq;

namespace _42.Platform.Cli.UnitTests.Commands;

// Records the plain text of every line the command writes: WriteLine calls, and the documents of Write, WriteImportant
// and WriteHeader (which render a document and then end the line). Themed spans are reduced to their text.
internal sealed class RecordingConsole
{
    private readonly List<string> _lines = [];
    private readonly StringBuilder _pending = new();

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
            .Callback<object[]>(elements =>
            {
                _pending.Append(string.Concat(elements.Select(ToText)));
                _lines.Add(_pending.ToString());
                _pending.Clear();
            });
        Mock.Setup(console => console.Write(It.IsAny<object[]>()))
            .Callback<object[]>(elements => _pending.Append(string.Concat(elements.Select(ToText))));
        Mock.Setup(console => console.Write(It.IsAny<Document>()))
            .Callback<Document>(document => _pending.Append(ToText(document)));
        Mock.Setup(console => console.WriteExactDocument(It.IsAny<Document>()))
            .Callback<Document>(document => _pending.Append(ToText(document)));
    }

    public Mock<IExtendedConsole> Mock { get; } = new();

    public IExtendedConsole Object => Mock.Object;

    public IReadOnlyList<string> Lines => _lines;

    // The plain text of an element: also of the cells of a table.
    internal static string ToText(object? element)
    {
        switch (element)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case Span span:
                return span.Text;
            case IEnumerable items:
                return string.Concat(items.Cast<object?>().Select(ToText));
        }

        // Document, Div and the other containers: their children, whatever the base type
        if (element.GetType().GetProperty("Children")?.GetValue(element) is IEnumerable children)
        {
            return string.Concat(children.Cast<object?>().Select(ToText));
        }

        return element.ToString() ?? string.Empty;
    }
}
