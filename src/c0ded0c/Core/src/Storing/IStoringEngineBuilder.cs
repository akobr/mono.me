using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IStoringEngineBuilder
    {
        public IStoringEngineBuilder SetStorer<TStorer>()
            where TStorer : IStorer;

        Task<IStoringEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default);
    }
}
