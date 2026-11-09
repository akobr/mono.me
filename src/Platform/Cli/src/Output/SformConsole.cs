using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;
using ShellProgressBar;

namespace _42.Platform.Cli.Output;

// The console of sform: the toolkit's ExtendedConsole, unless the process has no console at all. The toolkit renders its
// documents with Alba.CsConsoleFormat at the width of Console.BufferWidth, which throws "The handle is invalid" on
// Windows when standard input, output and error are all redirected (how a program, a CI step or the automation tests run
// sform: every themed line crashed it), and can be 0 on Linux and macOS without a terminal. Then a document is rendered
// as plain text at a fixed width (COLUMNS, else 250 characters), each row as a line without its trailing spaces. With a
// console nothing changes: every call goes to the toolkit.
// The fix belongs in the toolkit's ExtendedConsole (src/Libraries/CLI/Toolkit); this class can go once it is there.
public sealed class SformConsole : IExtendedConsole
{
    public const int DEFAULT_WIDTH_WITHOUT_CONSOLE = 250;
    public const int MIN_WIDTH_WITHOUT_CONSOLE = 20;

    private readonly ExtendedConsole _inner;
    private readonly Lazy<bool> _hasConsole;
    private readonly int _widthWithoutConsole;

    public SformConsole(IConsole console)
        : this(console, ReadConsoleWidth, Environment.GetEnvironmentVariable)
    {
    }

    // consoleWidth: the width of the console, null (or 0) when there is none.
    internal SformConsole(IConsole console, Func<int?> consoleWidth, Func<string, string?> getVariable)
    {
        _inner = new ExtendedConsole(console);
        _hasConsole = new Lazy<bool>(() => consoleWidth() is > 0);
        _widthWithoutConsole = ReadColumns(getVariable);
    }

    public IConsole Console => _inner.Console;

    public IConsoleTheme Theme
    {
        get => _inner.Theme;
        set => _inner.Theme = value;
    }

    public bool HasMainProgressBar => _inner.HasMainProgressBar;

    // False when sform renders its documents as plain text at a fixed width.
    public bool HasConsole => _hasConsole.Value;

    public void Write(params object[] elements)
    {
        if (HasConsole)
        {
            _inner.Write(elements);
            return;
        }

        Document document = new();
        document.Children.Add(elements);
        Write(document);
    }

    public void WriteLine(params object[] elements)
    {
        if (HasConsole)
        {
            _inner.WriteLine(elements);
            return;
        }

        if (elements.Length > 0)
        {
            Document document = new();
            document.Children.Add(elements);
            Write(document);
        }

        Console.WriteLine();
    }

    public void WriteTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction)
    {
        // Plain text written straight to the output: no width involved.
        _inner.WriteTree(root, nodeRenderFunction);
    }

    public void WriteTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction)
    {
        // What the toolkit does, with the nodes written through this console.
        new TreeOutputConsoleRenderer(this).RenderTree(root, nodeRenderFunction, 1);
    }

    public void WriteTable<T>(IEnumerable<T> rows, Func<T, IEnumerable<Cell>> rowRenderFunction, IEnumerable<IHeaderColumn>? headers = null)
    {
        if (HasConsole)
        {
            _inner.WriteTable(rows, rowRenderFunction, headers);
            return;
        }

        WriteExactDocument(new Document(CreateTable(rows, rowRenderFunction, headers, Theme)));
        Console.WriteLine();
    }

    public void Write(Document document)
    {
        if (HasConsole)
        {
            _inner.Write(document);
            return;
        }

        // The colour and the margins of the toolkit's Write.
        document.Color = Theme.ForegroundColor;
        document.Margin = Thickness.Add(document.Margin, new Thickness(2, 0, 2, 0));
        Render(document);
    }

    public void WriteExactDocument(Document document)
    {
        if (HasConsole)
        {
            _inner.WriteExactDocument(document);
            return;
        }

        Render(document);
    }

    // The prompts are the toolkit's; on redirected input no command reaches one (InteractiveInputExtensions).
    public T Input<T>(InputOptions<T> options)
    {
        return _inner.Input(options);
    }

    public bool Confirm(ConfirmOptions options)
    {
        return _inner.Confirm(options);
    }

    public string Password(PasswordOptions options)
    {
        return _inner.Password(options);
    }

    // The toolkit's IPrompter declares these without the notnull constraint of Sharprompt, so an implementation cannot add it.
#pragma warning disable CS8714
    public IEnumerable<T> List<T>(ListOptions<T> options)
    {
        return _inner.List(options);
    }

    public T Select<T>(SelectOptions<T> options)
    {
        return _inner.Select(options);
    }

    public IEnumerable<T> MultiSelect<T>(MultiSelectOptions<T> options)
    {
        return _inner.MultiSelect(options);
    }
#pragma warning restore CS8714

    public IProgressBar? GetMainProgressBar()
    {
        return _inner.GetMainProgressBar();
    }

    public IProgressBar StartProgressBar(string message, ProgressBarOptions? options = null)
    {
        return _inner.StartProgressBar(message, options);
    }

    public void EndMainProgressBar()
    {
        _inner.EndMainProgressBar();
    }

    // COLUMNS when it is a number, at least MIN_WIDTH_WITHOUT_CONSOLE; else DEFAULT_WIDTH_WITHOUT_CONSOLE.
    internal static int ReadColumns(Func<string, string?> getVariable)
    {
        return int.TryParse(getVariable(SformVariables.COLUMNS)?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var columns) && columns > 0
            ? Math.Max(columns, MIN_WIDTH_WITHOUT_CONSOLE)
            : DEFAULT_WIDTH_WITHOUT_CONSOLE;
    }

    private static int? ReadConsoleWidth()
    {
        try
        {
            return System.Console.BufferWidth;
        }
        catch (IOException)
        {
            // Windows: no standard handle is a console.
            return null;
        }
    }

    // The grid of the toolkit's WriteTable.
    private static Grid CreateTable<T>(IEnumerable<T> rows, Func<T, IEnumerable<Cell>> rowRenderFunction, IEnumerable<IHeaderColumn>? headers, IConsoleTheme theme)
    {
        var grid = new Grid
        {
            Stroke = LineThickness.None,
            Color = theme.ForegroundColor,
            Margin = new Thickness(1, 0),
        };

        var headerList = headers?.ToList() ?? [];

        if (headerList.Count > 0)
        {
            grid.Columns.Add(headerList.Select(header => header.Size).OfType<object>().ToArray());
            grid.Children.Add(headerList
                .Select(header => new Cell(header.Content) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) })
                .ToList());
            grid.Children.Add(new Cell(new Separator())
            {
                ColumnSpan = headerList.Count,
                Stroke = LineThickness.None,
            });
        }

        var rowCells = Array.Empty<object>();

        foreach (var row in rows)
        {
            rowCells = rowRenderFunction(row).OfType<object>().ToArray();
            grid.Children.Add(rowCells);
        }

        if (headerList.Count == 0)
        {
            grid.Columns.Add(rowCells.Select(_ => GridLength.Auto).OfType<object>().ToArray());
        }

        return grid;
    }

    // Each row of the document as a line without its trailing spaces. The last row is not ended, as on a console, where
    // the toolkit's WriteLine ends it.
    private void Render(Document document)
    {
        using var target = new TextRenderTarget(new StringWriter(CultureInfo.InvariantCulture));
        var text = ConsoleRenderer.RenderDocumentToText(document, target, new Rect(0, 0, _widthWithoutConsole, Size.Infinity));
        var lines = text.Split('\n').Select(line => line.TrimEnd()).ToList();

        // The text ends with a line break, which leaves an empty entry.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        Console.Out.Write(string.Join(Console.Out.NewLine, lines));
    }
}
