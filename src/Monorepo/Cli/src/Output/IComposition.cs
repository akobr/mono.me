using System.Collections.Generic;

namespace _42.Monorepo.Cli.Output
{
    public interface IComposition<out T>
    {
        T Content { get; }

        IReadOnlyCollection<IComposition<T>> Children { get; }
    }
}
