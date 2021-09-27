using System.Collections.Generic;

namespace _42.Monorepo.Cli.Output
{
    public class Composition<T> : IComposition<T>
    {
        public Composition(T content)
        {
            Content = content;
            Children = new List<IComposition<T>>();
        }

        public T Content { get; set; }

        public List<IComposition<T>> Children { get; set; }

        IReadOnlyCollection<IComposition<T>> IComposition<T>.Children => Children;
    }
}
