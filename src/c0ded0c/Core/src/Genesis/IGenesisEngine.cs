using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public interface IGenesisEngine : IEngine
    {
        Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace);
    }
}
