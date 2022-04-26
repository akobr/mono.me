using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public interface IGenesisEngineBuilder : IEngineBuilder<IWorkspaceInfo, IGenesisMiddleware>
    {
        Task<IGenesisEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default);
    }
}
