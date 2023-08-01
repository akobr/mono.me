using System;

namespace _42.CLI.Toolkit.Output
{
    public interface ITreeOutputRenderer
    {
        void RenderTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction, int leftIndentation = 0);
    }
}
