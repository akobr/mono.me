using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using _42.CLI.Toolkit.Output;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Moq;

namespace _42.Platform.Cli.UnitTests.Commands;

// Records the plain text of every line the command writes: WriteLine calls, and the documents of Write, WriteImportant
// and WriteHeader (which render a document and then end the line). Themed spans are reduced to their text. Tables are
// recorded apart from the lines, as their headers and the text of each cell.
internal sealed class RecordingConsole
{
    private readonly List<string> _lines = [];
    private readonly List<RecordedTable> _tables = [];
    private readonly StringBuilder _pending = new();
    private readonly StringWriter _error = new();

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
        Mock.Setup(console => console.WriteTable(
                It.IsAny<IEnumerable<It.IsAnyType>>(),
                It.IsAny<Func<It.IsAnyType, IEnumerable<Cell>>>(),
                It.IsAny<IEnumerable<IHeaderColumn>?>()))
            .Callback(new InvocationAction(invocation =>
            {
                var rows = (IEnumerable)invocation.Arguments[0];
                var render = (Delegate)invocation.Arguments[1];
                var headers = (IEnumerable<IHeaderColumn>?)invocation.Arguments[2];

                _tables.Add(new RecordedTable(
                    headers?.Select(header => ToText(header.Content)).ToList() ?? [],
                    rows.Cast<object?>()
                        .Select(row => ((IEnumerable<Cell>)render.DynamicInvoke(row)!).Select(cell => ToText(cell)).ToList())
                        .ToList()));
            }));
    }

    public Mock<IExtendedConsole> Mock { get; } = new();

    public IExtendedConsole Object => Mock.Object;

    public IReadOnlyList<string> Lines => _lines;

    public IReadOnlyList<RecordedTable> Tables => _tables;

    // What went to standard error of the inner console, after UseInnerConsole.
    public string ErrorText => _error.ToString();

    public IReadOnlyList<string> ErrorLines => ErrorText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    // An inner console (Console.Console) whose standard error is recorded; with redirectedInput, the standard input is
    // redirected and reads the given text, as for a pipe or a job without a terminal.
    public RecordingConsole UseInnerConsole(bool redirectedInput, string input = "")
    {
        var inner = new Mock<IConsole>();
        inner.SetupGet(console => console.IsInputRedirected).Returns(redirectedInput);
        inner.SetupGet(console => console.Error).Returns(_error);
        inner.SetupGet(console => console.Out).Returns(TextWriter.Null);
        inner.SetupGet(console => console.In).Returns(new StringReader(input));
        Mock.SetupGet(console => console.Console).Returns(inner.Object);
        return this;
    }

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
