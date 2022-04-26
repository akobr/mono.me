using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public interface IAssemblyProcessingEngine : IEngine
    {
        Task<IAssemblyInfo> ProcessAsync(IAssemblyInfo assembly, CancellationToken cancellation = default);
    }
}
