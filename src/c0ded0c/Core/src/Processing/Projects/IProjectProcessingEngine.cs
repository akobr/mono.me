using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IProjectProcessingEngine : IEngine
    {
        Task<IProjectInfo> ProcessAsync(IProjectInfo project, CancellationToken cancellation = default);
    }
}
