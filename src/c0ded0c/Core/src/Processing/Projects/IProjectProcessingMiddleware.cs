using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IProjectProcessingMiddleware : IMiddleware
    {
        Task<IProjectInfo> ProcessAsync(IProjectInfo project, ProjectProcessingAsyncDelegate next);
    }
}
