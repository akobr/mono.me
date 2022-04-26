using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IAssemblyProcessingMiddleware : IMiddleware
    {
        Task<IAssemblyInfo> ProcessAsync(IAssemblyInfo assembly, AssemblyProcessingAsyncDelegate next);
    }
}
