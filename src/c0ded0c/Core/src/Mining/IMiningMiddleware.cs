using System.Collections.Immutable;
using System.Threading.Tasks;

namespace c0ded0c.Core.Mining
{
    public interface IMiningMiddleware : IMiddleware
    {
        Task<IImmutableSet<IProjectInfo>> MineAsync(string path, MiningAsyncDelegate next);
    }
}
