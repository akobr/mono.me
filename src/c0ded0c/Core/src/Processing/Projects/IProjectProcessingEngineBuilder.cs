using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IProjectProcessingEngineBuilder : IEngineBuilder<IProjectInfo, IProjectProcessingMiddleware>
    {
        Task<IProjectProcessingEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default);
    }
}
