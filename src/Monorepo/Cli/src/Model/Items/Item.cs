using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;
using _42.Monorepo.Cli.Versioning;
using Semver;

namespace _42.Monorepo.Cli.Model.Items
{
    public class Item : IItem
    {
        private readonly IOpsExecutor executor;
        private readonly Lazy<IItem?> parent;

        public Item(IRecord record, IOpsExecutor executor, Func<IRecord, IItem> itemFactory)
        {
            this.executor = executor;
            Record = record;
            ItemFactory = itemFactory;
            parent = new(() => record.Parent is null ? null : itemFactory(record.Parent));
        }

        public IRecord Record { get; }

        public IItem? Parent => parent.Value;

        protected IOpsExecutor Executor => executor;

        protected Func<IRecord, IItem> ItemFactory { get; }

        public static bool operator ==(Item? left, IItem? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Item? left, IItem? right)
        {
            return !Equals(left, right);
        }

        public virtual IEnumerable<IItem> GetChildren()
        {
            return Enumerable.Empty<IItem>();
        }

        public Task<string?> TryGetVersionFilePathAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<string?>(this, cancellationToken: cancellationToken);

        public Task<IVersionTemplate?> TryGetDefinedVersionAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<IVersionTemplate?>(this, cancellationToken: cancellationToken);

        public Task<IExactVersions> GetExactVersionsAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<IExactVersions>(this, cancellationToken: cancellationToken);

        public Task<IRelease?> TryGetLastReleaseAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<IRelease?>(this, cancellationToken: cancellationToken);

        public Task<IReadOnlyList<IRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<IReadOnlyList<IRelease>>(this, cancellationToken: cancellationToken);

        public Task<string?> TryGetPackagesFilePathAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<string?>(this, cancellationToken: cancellationToken);

        public Task<IReadOnlyCollection<IExternalDependency>> GetExternalDependenciesAsync(CancellationToken cancellationToken = default)
            => executor.ExecuteAsync<IReadOnlyCollection<IExternalDependency>>(this, cancellationToken: cancellationToken);

        public bool Equals(IItem? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Record.Equals(other.Record);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj is IItem item && Equals(item);
        }

        public override int GetHashCode()
        {
            return Record.GetHashCode();
        }
    }
}
