using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core.Mining
{
    public interface IMiningEngineBuilder : IEngineBuilder<string, IImmutableSet<IProjectInfo>, IMiningMiddleware>
    {
        Task<IMiningEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default);
    }
}
