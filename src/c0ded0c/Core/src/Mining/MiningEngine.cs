using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using c0ded0c.Core.Configuration;

namespace c0ded0c.Core.Mining
{
    public class MiningEngine : BaseEngine<string, IImmutableSet<IProjectInfo>>, IMiningEngine
    {
        private static Func<string, Task<IImmutableSet<IProjectInfo>>> dumpMiddleware =
            (path) => Task.FromResult<IImmutableSet<IProjectInfo>>(ImmutableHashSet<IProjectInfo>.Empty);

        public MiningEngine(
            IImmutableList<Func<string, Func<string, Task<IImmutableSet<IProjectInfo>>>, Task<IImmutableSet<IProjectInfo>>>> middlewares,
            IImmutableDictionary<string, string> properties)
            : base(middlewares, dumpMiddleware, properties)
        {
            // no operation
        }

        public async Task<IImmutableSet<IProjectInfo>> MineAsync(CancellationToken cancellation = default)
        {
            var projects = ImmutableHashSet<IProjectInfo>.Empty;

            foreach (string path in Properties.GetEnumerable(PropertyNames.InputPaths))
            {
                projects = projects.Union(await Run(path));
            }

            return projects;
        }
    }
}
