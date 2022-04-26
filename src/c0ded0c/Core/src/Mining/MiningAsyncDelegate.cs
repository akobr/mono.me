using System.Collections.Immutable;
using System.Threading.Tasks;

namespace c0ded0c.Core.Mining
{
    public delegate Task<IImmutableSet<IProjectInfo>> MiningAsyncDelegate(string path);
}
