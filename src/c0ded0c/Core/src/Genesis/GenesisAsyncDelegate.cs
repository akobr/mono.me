using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public delegate Task<IWorkspaceInfo> GenesisAsyncDelegate(IWorkspaceInfo workspace);
}
