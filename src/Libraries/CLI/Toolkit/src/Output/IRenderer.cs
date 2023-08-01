using System;
using System.Collections.Generic;
using Alba.CsConsoleFormat;

namespace _42.CLI.Toolkit.Output
{
    public interface IRenderer
    {
        public IConsoleTheme Theme { get; }

        void Write(params object[] elements);

        void WriteLine(params object[] elements);

        void WriteTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction);

        void WriteTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction);

        void WriteTable<T>(IEnumerable<T> rows, Func<T, IEnumerable<Cell>> rowRenderFunction, IEnumerable<IHeaderColumn>? headers = null);

        void Write(Document document);

        void WriteExactDocument(Document document);
    }
}
