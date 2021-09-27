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

        public static void WriteHeader(this IRenderer renderer, params object[] elements)
        {
            Document document = new()
            {
                Color = renderer.Theme.HeaderColor,
                Children =
                {
                    new Span("# "),
                    elements,
                    new Br(),
                },
            };

            renderer.WriteExactDocument(document);
        }

        public static void WriteImportant(this IRenderer renderer, params object[] elements)
        {
            Document document = new()
            {
                Children =
                {
                    "! ".Colored(renderer.Theme.HighlightColor),
                    elements,
                    new Br(),
                },
            };

            renderer.WriteExactDocument(document);
        }

        public static void WriteTable<T>(
            this IRenderer renderer,
            IEnumerable<T> rows,
            Func<T, IEnumerable<string>> rowRenderFunction,
            IEnumerable<IHeaderColumn>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => rowRenderFunction(r).Select(c => new Cell(c) { Stroke = LineThickness.None }),
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
                r => rowRenderFunction(r).Select(c => new Cell(c) { Stroke = LineThickness.None }),
                headers?.Select(text => new HeaderColumn(text)));
        }

        public static void WriteTable<T>(
            this IRenderer renderer,
            IEnumerable<T> rows,
            Func<T, IEnumerable<object>> rowRenderFunction,
            IEnumerable<IHeaderColumn>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => rowRenderFunction(r).Select(c => new Cell(c.ToString()) { Stroke = LineThickness.None }),
                headers);
        }

        public static void WriteTable<T>(
            this IRenderer renderer,
            IEnumerable<T> rows,
            Func<T, IEnumerable<object>> rowRenderFunction,
            IEnumerable<string>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => rowRenderFunction(r).Select(c => new Cell(c.ToString()) { Stroke = LineThickness.None }),
                headers?.Select(text => new HeaderColumn(text)));
        }

        public static void WriteTable(
            this IRenderer renderer,
            IEnumerable<IEnumerable<string>> rows,
            IEnumerable<IHeaderColumn>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => r.Select(c => new Cell(c) { Stroke = LineThickness.None }),
                headers);
        }

        public static void WriteTable(
            this IRenderer renderer,
            IEnumerable<IEnumerable<string>> rows,
            IEnumerable<string>? headers = null)
        {
            renderer.WriteTable(
                rows,
                r => r.Select(c => new Cell(c) { Stroke = LineThickness.None }),
                headers?.Select(text => new HeaderColumn(text)));
        }
    }
}
