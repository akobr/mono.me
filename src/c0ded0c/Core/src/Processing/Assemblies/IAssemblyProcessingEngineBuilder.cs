using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IAssemblyProcessingEngineBuilder : IEngineBuilder<IAssemblyInfo, IAssemblyProcessingMiddleware>
    {
        Task<IAssemblyProcessingEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default);
    }
}
