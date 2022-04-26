using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface IInitialisable<TContext>
    {
        void Initialise(TContext context);
    }

    public interface IInitialisableWithProperties : IInitialisable<IImmutableDictionary<string, string>>
    {
        // no member
    }
}
