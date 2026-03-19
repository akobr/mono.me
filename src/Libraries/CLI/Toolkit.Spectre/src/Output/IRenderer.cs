using System;
using System.Collections.Generic;

namespace _42.CLI.Toolkit.Output;

public interface IRenderer
{
    IConsoleTheme Theme { get; set; }

    void Write(params object[] elements);

    void WriteLine(params object[] elements);

    void WriteTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction);

    void WriteTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction);

    void WriteTable<T>(IEnumerable<T> rows, Func<T, IEnumerable<string>> rowRenderFunction, IEnumerable<IHeaderColumn>? headers = null);
}
