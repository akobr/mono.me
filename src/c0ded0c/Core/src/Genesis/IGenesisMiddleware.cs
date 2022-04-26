using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public interface IGenesisMiddleware : IMiddleware
    {
        Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next);
    }
}
