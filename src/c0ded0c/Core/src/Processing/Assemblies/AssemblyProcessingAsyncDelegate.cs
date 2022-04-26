using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public delegate Task<IAssemblyInfo> AssemblyProcessingAsyncDelegate(IAssemblyInfo assembly);
}
