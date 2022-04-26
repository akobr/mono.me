using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class AssemblyProcessingEngine : BaseEngine<IAssemblyInfo, IAssemblyInfo>, IAssemblyProcessingEngine
    {
        private static Func<IAssemblyInfo, Task<IAssemblyInfo>> dumpMiddleware = (assembly) => Task.FromResult(assembly);

        public AssemblyProcessingEngine(
            IImmutableList<Func<IAssemblyInfo, Func<IAssemblyInfo, Task<IAssemblyInfo>>, Task<IAssemblyInfo>>> middlewares,
            IImmutableDictionary<string, string> properties)
            : base(middlewares, dumpMiddleware, properties)
        {
        }

        public Task<IAssemblyInfo> ProcessAsync(IAssemblyInfo assembly, CancellationToken cancellation = default)
        {
            return Run(assembly);
        }
    }
}
