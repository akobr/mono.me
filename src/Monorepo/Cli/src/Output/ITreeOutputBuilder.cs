using System;

namespace _42.Monorepo.Cli.Output
{
    public interface ITreeOutputBuilder
    {
        string BuildTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction, int leftIndentation = 0);
    }
}
