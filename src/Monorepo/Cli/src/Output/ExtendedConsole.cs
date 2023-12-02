using System;
using System.Collections.Generic;
using System.Linq;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;
using ShellProgressBar;
using Prompt = Sharprompt.Prompt;

namespace _42.Monorepo.Cli.Output
{
    public class ExtendedConsole : IExtendedConsole
    {
        private readonly ITreeOutputBuilder treeBuilder;
        private ProgressBar? activeProgressBar;

        public ExtendedConsole(IConsole console)
        {
            Console = console;
            treeBuilder = new TreeOutputBuilder();

            // TODO: [p3] make it configurable
            Theme = new ConsoleTheme()
            {
                ForegroundColor = ConsoleColor.Gray,
                HeaderColor = ConsoleColor.White,
                HighlightColor = ConsoleColor.Magenta,
                LowlightColor = ConsoleColor.DarkGray,
                ErrorColor = ConsoleColor.Red,
            };
        }

        public IConsole Console { get; }

        public IConsoleTheme Theme { get; }

        public bool HasMainProgressBar => activeProgressBar is not null;

        public void Write(params object[] elements)
        {
            Document document = new();
            document.Children.Add(elements);
            Write(document);
        }

        public void WriteLine(params object[] elements)
        {
            if (elements.Length < 1)
            {
                Console.WriteLine();
                return;
            }

            Document document = new();
            document.Children.Add(elements);
            Write(document);
            Console.WriteLine();
        }

        public void WriteTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction)
        {
            Console.Write(treeBuilder.BuildTree(root, nodeRenderFunction, 1));
        }

        public void WriteTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction)
        {
            TreeOutputConsoleRenderer renderer = new(this);
            renderer.RenderTree(root, nodeRenderFunction, 1);
        }

        public void WriteTable<T>(
            IEnumerable<T> rows,
            Func<T, IEnumerable<Cell>> rowRenderFunction,
            IEnumerable<IHeaderColumn>? headers = null)
        {
            var grid = new Grid
            {
                Stroke = LineThickness.None,
                Color = Theme.ForegroundColor,
                Margin = new Thickness(1, 0),
            };

            var noHeaders = true;

            if (headers is not null)
            {
                var headerList = headers.ToList();

                if (headerList.Count > 0)
                {
                    noHeaders = false;

                    var headerCells = headerList
                        .Select(header => new Cell(header.Content) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) })
                        .ToList();

                    grid.Columns.Add(headerList
                        .Select(header => header.Size)
                        .OfType<object>()
                        .ToArray());

                    grid.Children.Add(headerCells);
                    grid.Children.Add(
                        new Cell(new Separator())
                        {
                            ColumnSpan = headerList.Count,
                            Stroke = LineThickness.None,
                        });
                }
            }

            object[] rowCells = Array.Empty<object>();

            foreach (var row in rows)
            {
                rowCells = rowRenderFunction(row)
                    .OfType<object>()
                    .ToArray();

                grid.Children.Add(rowCells);
            }

            if (noHeaders)
            {
                grid.Columns.Add(rowCells
                    .Select(c => GridLength.Auto)
                    .OfType<object>()
                    .ToArray());
            }

            WriteExactDocument(new Document(grid));
            Console.WriteLine();
        }

        public void Write(Document document)
        {
            document.Color = Theme.ForegroundColor;
            document.Margin = Thickness.Add(document.Margin, new Thickness(2, 0, 2, 0));
            ConsoleRenderer.RenderDocument(document);
        }

        public void WriteExactDocument(Document document)
        {
            ConsoleRenderer.RenderDocument(document);
        }

        public T Input<T>(InputOptions<T> options)
        {
            return Prompt.Input<T>(options);
        }

        public bool Confirm(ConfirmOptions options)
        {
            return Prompt.Confirm(options);
        }

        public string Password(PasswordOptions options)
        {
            return Prompt.Password(options);
        }

        public IEnumerable<T> List<T>(ListOptions<T> options)
        {
            return Prompt.List(options);
        }

        public T Select<T>(SelectOptions<T> options)
        {
            return Prompt.Select(options);
        }

        public IEnumerable<T> MultiSelect<T>(MultiSelectOptions<T> options)
        {
            return Prompt.MultiSelect(options);
        }

        public IProgressBar? GetMainProgressBar()
        {
            return activeProgressBar;
        }

        public IProgressBar StartProgressBar(string message, ProgressBarOptions? options = null)
        {
            if (activeProgressBar is not null)
            {
                return activeProgressBar.Spawn(100, message, options);
            }

            return activeProgressBar = new ProgressBar(100, message, options);
        }

        public void EndMainProgressBar()
        {
            activeProgressBar?.Dispose();
            activeProgressBar = null;
        }
    }
}
