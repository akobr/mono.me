using System;

namespace _42.CLI.Toolkit.Output;

public interface ITreeOutputBuilder
{
    string BuildTree<T>(IComposition<T> root, Func<T, string> nodeRenderFunction, int leftIndentation = 0);
}
