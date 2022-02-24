using System;
using System.Collections.Generic;
using System.Linq;
using Alba.CsConsoleFormat;

namespace _42.Monorepo.Cli.Output
{
    public static class RendererExtensions
    {
        public static Span Colored(this string @this, ConsoleColor color)
        {
            return new(@this)
            {
                Color = new ConsoleColor?(color),
            };
        }

        public static Span ThemedHeader(this string @this, IConsoleTheme theme)
        {
            return new(@this)
            {
                Color = new ConsoleColor?(theme.HeaderColor),
            };
        }

        public static Span ThemedHighlight(this string @this, IConsoleTheme theme)
        {
            return new(@this)
            {
                Color = new ConsoleColor?(theme.HighlightColor),
            };
        }

        public static Span ThemedLowlight(this string @this, IConsoleTheme theme)
        {
            return new(@this)
            {
                Color = new ConsoleColor?(theme.LowlightColor),
            };
        }

        public static Span ThemedError(this string @this, IConsoleTheme theme)
        {
            return new(@this)
            {
                Color = new ConsoleColor?(theme.LowlightColor),
            };
        }

        public static void WriteHeader(this IRenderer renderer, params object[] elements)
        {
            Document document = new()
            {
                Color = renderer.Theme.HeaderColor,
                Children =
                {
                    new Span("# "),
                    elements,
                },
            };

            renderer.WriteExactDocument(document);
            renderer.WriteLine();
        }

        public static void WriteImportant(this IRenderer renderer, params object[] elements)
        {
            Document document = new()
            {
                Children =
                {
                    "! ".Colored(renderer.Theme.HighlightColor),
                    elements,
                },
            };

            renderer.WriteExactDocument(document);
            renderer.WriteLine();
        }

        public static void WriteTable<T>(
            this IRenderer renderer,
            IEnumerable<T> rows,
            Func<T, IEnumerable<string>> rowRenderFunction,
            IEnumerable<IHeaderColumn>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => rowRenderFunction(r).Select(c => new Cell(c) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }),
                headers);
        }

        public static void WriteTable<T>(
            this IRenderer renderer,
            IEnumerable<T> rows,
            Func<T, IEnumerable<string>> rowRenderFunction,
            IEnumerable<string>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => rowRenderFunction(r).Select(c => new Cell(c) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }),
                headers?.Select(text => new HeaderColumn(text)));
        }

        public static void WriteTable(
            this IRenderer renderer,
            IEnumerable<IEnumerable<string>> rows,
            IEnumerable<IHeaderColumn>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => r.Select(c => new Cell(c) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }),
                headers);
        }

        public static void WriteTable(
            this IRenderer renderer,
            IEnumerable<IEnumerable<string>> rows,
            IEnumerable<string>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => r.Select(c => new Cell(c) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }),
                headers?.Select(text => new HeaderColumn(text)));
        }
    }
}
