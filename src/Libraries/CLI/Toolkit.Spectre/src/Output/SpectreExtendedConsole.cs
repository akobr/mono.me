using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using Spectre.Console;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace _42.CLI.Toolkit.Output;

public class SpectreExtendedConsole : IExtendedConsole
{
    private readonly IAnsiConsole _ansiConsole;
    private readonly ITreeOutputBuilder _treeBuilder;

    // Progress state
    private ProgressContext? _progressContext;
    private ManualResetEventSlim? _progressDoneEvent;
    private SpectreProgressBar? _mainProgressBar;
    private int _activeProgressBars;

    public SpectreExtendedConsole(IConsole console, IAnsiConsole ansiConsole)
    {
        Console = console;
        _ansiConsole = ansiConsole;
        _treeBuilder = new TreeOutputBuilder();

        Theme = new ConsoleTheme
        {
            ForegroundColor = ConsoleColor.Gray,
            HeaderColor = ConsoleColor.White,
            HighlightColor = ConsoleColor.Magenta,
            LowlightColor = ConsoleColor.DarkGray,
            ErrorColor = ConsoleColor.Red,
        };
    }

    public IConsole Console { get; }

    public IAnsiConsole AnsiConsole => _ansiConsole;

    public IConsoleTheme Theme { get; set; }

    // ── IRenderer ────────────────────────────────────────────────────────────

    public void Write(params object[] elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case null:
                    break;

                case IConsoleOutput output:
                    output.WriteTo(this);
                    break;

                case string text:
                    _ansiConsole.Write(new Text(text));
                    break;

                default:
                    _ansiConsole.Write(new Text(element.ToString() ?? string.Empty));
                    break;
            }
        }
    }

    public void WriteLine(params object[] elements)
    {
        if (elements.Length < 1)
        {
            _ansiConsole.WriteLine();
            return;
        }

        Write(elements);
        _ansiConsole.WriteLine();
    }

    public void WriteTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction)
    {
        var tree = new Tree(nodeRenderFunction(root.Content));
        BuildSpectreTreeNodes(tree, root, nodeRenderFunction);
        _ansiConsole.Write(tree);
        _ansiConsole.WriteLine();
    }

    public void WriteTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction)
    {
        var renderer = new TreeOutputConsoleRenderer(this);
        renderer.RenderTree(root, nodeRenderFunction, 1);
    }

    public void WriteTable<T>(IEnumerable<T> rows, Func<T, IEnumerable<string>> rowRenderFunction, IEnumerable<IHeaderColumn>? headers = null)
    {
        var table = new Table();
        table.Border(TableBorder.None);
        table.Expand();

        var headerList = headers?.ToList();
        var rowList = rows.Select(r => rowRenderFunction(r).ToList()).ToList();

        if (headerList?.Count > 0)
        {
            foreach (var header in headerList)
            {
                var column = new TableColumn(header.Content);
                if (header.Width.HasValue)
                {
                    column = column.Width(header.Width.Value);
                }

                table.AddColumn(column);
            }
        }
        else if (rowList.Count > 0)
        {
            for (var i = 0; i < rowList[0].Count; i++)
            {
                table.AddColumn(new TableColumn(string.Empty));
            }
        }
        else
        {
            return;
        }

        foreach (var row in rowList)
        {
            table.AddRow(row.Select(Markup.Escape).ToArray());
        }

        _ansiConsole.Write(table);
        _ansiConsole.WriteLine();
    }

    // ── IPrompter ────────────────────────────────────────────────────────────

    public T Input<T>(string message, T? defaultValue = default, IList<Func<object, DataAnnotations.ValidationResult>>? validators = null)
    {
        var prompt = new TextPrompt<T>(message);

        if (defaultValue is not null)
        {
            prompt = prompt.DefaultValue(defaultValue);
        }

        if (validators is not null)
        {
            foreach (var validator in validators)
            {
                prompt = prompt.Validate(value =>
                {
                    var result = validator(value!);
                    return result == DataAnnotations.ValidationResult.Success
                        ? ValidationResult.Success()
                        : ValidationResult.Error(result.ErrorMessage ?? "Invalid input.");
                });
            }
        }

        return _ansiConsole.Prompt(prompt);
    }

    public bool Confirm(string message, bool? defaultValue = null)
    {
        var prompt = new ConfirmationPrompt(message);
        if (defaultValue.HasValue)
        {
            prompt.DefaultValue = defaultValue.Value;
        }

        return _ansiConsole.Prompt(prompt);
    }

    public string Password(string message)
    {
        return _ansiConsole.Prompt(
            new TextPrompt<string>(message).Secret());
    }

    public IEnumerable<T> List<T>(string message)
    {
        var items = new List<T>();
        while (true)
        {
            var prompt = new TextPrompt<string>($"{message} (empty to stop):")
                .AllowEmpty();
            var input = _ansiConsole.Prompt(prompt);
            if (string.IsNullOrEmpty(input))
            {
                break;
            }

            try
            {
                var converted = (T)Convert.ChangeType(input, typeof(T));
                items.Add(converted);
            }
            catch
            {
                _ansiConsole.MarkupLine("[red]Invalid input, skipped.[/]");
            }
        }

        return items;
    }

    public T Select<T>(string message, IEnumerable<T> choices, Func<T, string>? converter = null)
    {
        var prompt = new SelectionPrompt<T>().Title(message);
        if (converter is not null)
        {
            prompt = prompt.UseConverter(converter);
        }

        prompt.AddChoices(choices);
        return _ansiConsole.Prompt(prompt);
    }

    public IEnumerable<T> MultiSelect<T>(string message, IEnumerable<T> choices, Func<T, string>? converter = null, int? minimum = null, int? maximum = null)
    {
        var prompt = new MultiSelectionPrompt<T>().Title(message);
        if (converter is not null)
        {
            prompt = prompt.UseConverter(converter);
        }

        if (minimum.HasValue)
        {
            prompt = prompt.Required(minimum.Value > 0);
        }

        prompt.AddChoices(choices);
        return _ansiConsole.Prompt(prompt);
    }

    // ── IProgressReporter ────────────────────────────────────────────────────

    public bool HasMainProgressBar => _mainProgressBar is not null;

    public IProgressBar? GetMainProgressBar() => _mainProgressBar;

    public IProgressBar StartProgressBar(string message, int maxTicks = 100)
    {
        if (_progressContext is null)
        {
            var contextReady = new ManualResetEventSlim();
            _progressDoneEvent = new ManualResetEventSlim();

            var thread = new Thread(() =>
            {
                _ansiConsole
                    .Progress()
                    .AutoClear(false)
                    .HideCompleted(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new ElapsedTimeColumn())
                    .Start(ctx =>
                    {
                        _progressContext = ctx;
                        contextReady.Set();
                        _progressDoneEvent.Wait();
                    });
            });

            thread.IsBackground = true;
            thread.Start();
            contextReady.Wait();
        }

        Interlocked.Increment(ref _activeProgressBars);
        var task = _progressContext!.AddTask(message, maxValue: maxTicks);
        var bar = new SpectreProgressBar(task, this);

        _mainProgressBar ??= bar;
        return bar;
    }

    public void EndMainProgressBar()
    {
        _progressDoneEvent?.Set();
        _progressContext = null;
        _mainProgressBar = null;
        _activeProgressBars = 0;
    }

    internal void OnProgressBarDisposed()
    {
        if (Interlocked.Decrement(ref _activeProgressBars) <= 0)
        {
            _progressDoneEvent?.Set();
            _progressContext = null;
            _mainProgressBar = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void BuildSpectreTreeNodes<T>(IHasTreeNodes parent, IComposition<T> node, Func<T, string> renderFunction)
    {
        foreach (var child in node.Children)
        {
            var childNode = parent.AddNode(renderFunction(child.Content));
            BuildSpectreTreeNodes(childNode, child, renderFunction);
        }
    }
}
