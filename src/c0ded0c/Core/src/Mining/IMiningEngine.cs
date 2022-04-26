using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core.Mining
{
    public interface IMiningEngine : IEngine
    {
        Task<IImmutableSet<IProjectInfo>> MineAsync(CancellationToken cancellation = default);
    }
}
