using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.CLI.Toolkit.Output;

public static class RendererExtensions
{
    public static ConsoleOutputText Colored(this string @this, ConsoleColor color)
    {
        return new(@this, color);
    }

    public static ConsoleOutputText ThemedHeader(this string @this, IConsoleTheme theme)
    {
        return new(@this, theme.HeaderColor);
    }

    public static ConsoleOutputText ThemedHighlight(this string @this, IConsoleTheme theme)
    {
        return new(@this, theme.HighlightColor);
    }

    public static ConsoleOutputText ThemedLowlight(this string @this, IConsoleTheme theme)
    {
        return new(@this, theme.LowlightColor);
    }

    public static ConsoleOutputText ThemedError(this string @this, IConsoleTheme theme)
    {
        return new(@this, theme.ErrorColor);
    }

    public static void WriteHeader(this IRenderer renderer, params object[] elements)
    {
        var allElements = new List<object> { new ConsoleOutputThemedText("# ", t => t.HeaderColor) };
        foreach (var e in elements)
        {
            allElements.Add(e is string s ? (object)new ConsoleOutputThemedText(s, t => t.HeaderColor) : e);
        }

        renderer.Write(allElements.ToArray());
        renderer.WriteLine();
    }

    public static void WriteImportant(this IRenderer renderer, params object[] elements)
    {
        var allElements = new List<object> { new ConsoleOutputThemedText("! ", t => t.HighlightColor) };
        allElements.AddRange(elements);

        renderer.Write(allElements.ToArray());
        renderer.WriteLine();
    }

    public static void WriteTree(this IRenderer renderer, Composition composition)
    {
        renderer.WriteTree(composition, n => n);
    }

    public static void WriteTable<T>(
        this IRenderer renderer,
        IEnumerable<T> rows,
        Func<T, IEnumerable<string>> rowRenderFunction,
        IEnumerable<IHeaderColumn>? headers = null)
    {
        renderer.WriteTable(rows, rowRenderFunction, headers);
    }

    public static void WriteTable<T>(
        this IRenderer renderer,
        IEnumerable<T> rows,
        Func<T, IEnumerable<string>> rowRenderFunction,
        IEnumerable<string>? headers = null)
    {
        renderer.WriteTable(rows, rowRenderFunction, headers?.Select(text => new HeaderColumn(text)));
    }

    public static void WriteTable(
        this IRenderer renderer,
        IEnumerable<IEnumerable<string>> rows,
        IEnumerable<IHeaderColumn>? headers = null)
    {
        renderer.WriteTable(rows, r => r, headers);
    }

    public static void WriteTable(
        this IRenderer renderer,
        IEnumerable<IEnumerable<string>> rows,
        IEnumerable<string>? headers = null)
    {
        renderer.WriteTable(rows, r => r, headers?.Select(text => new HeaderColumn(text)));
    }
}
