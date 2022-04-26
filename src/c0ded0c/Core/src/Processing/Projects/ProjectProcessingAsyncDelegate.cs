using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public delegate Task<IProjectInfo> ProjectProcessingAsyncDelegate(IProjectInfo project);
}
