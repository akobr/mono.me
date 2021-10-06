using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;

namespace _42.Monorepo.Cli.Model.Items
{
    public class Project : Item, IProject
    {
        public Project(IProjectRecord item, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
            : base(item, executor, itemFactory)
        {
            Record = item;
        }

        public new IProjectRecord Record { get; }

        public Task<IReadOnlyCollection<IInternalDependency>> GetInternalDependenciesAsync(CancellationToken cancellationToken = default)
            => Executor.ExecuteAsync<IReadOnlyCollection<IInternalDependency>>(this, cancellationToken: cancellationToken);

        public Task<bool> GetIsPackableAsync(CancellationToken cancellationToken = default)
            => Executor.ExecuteAsync<bool>(this, cancellationToken: cancellationToken);

        public Task<string> GetPackageNameAsync(CancellationToken cancellationToken = default)
            => Executor.ExecuteAsync<string>(this, cancellationToken: cancellationToken);
    }
}
