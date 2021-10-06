using System;

namespace _42.Monorepo.Cli.Output
{
    public interface ITreeOutputRenderer
    {
        void RenderTree<T>(IComposition<T> root, Func<T, IConsoleOutput> nodeRenderFunction, int leftIndentation = 0);
    }
}
