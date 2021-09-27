using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;
using Semver;

namespace _42.Monorepo.Cli.Model.Items
{
    public class Item : IItem
    {
        private readonly Lazy<IItem?> parent;
        private readonly AsyncLazy<string?> versionFilePath;
        private readonly AsyncLazy<SemVersion?> definedVersion;
        private readonly AsyncLazy<SemVersion> exactVersion;
        private readonly AsyncLazy<IReadOnlyList<IRelease>> allReleases;
        private readonly AsyncLazy<IRelease?> lastRelease;
        private readonly AsyncLazy<IReadOnlyCollection<IExternalDependency>> externalDependencies;

        public Item(IItemRecord record, IOperationsCache cache, Func<IItemRecord, IItem> itemFactory)
        {
            Record = record;
            ItemFactory = itemFactory;
            parent = new(() => record.Parent is null ? null : itemFactory(record.Parent));

            versionFilePath = new(
                t => ProcessOperation(cache.TryGetVersionFilePath, CalculateVersionFilePathAsync, cache.StoreVersionFilePath, t));
            definedVersion = new(
                t => ProcessOperation(cache.TryGetDefinedVersion, CalculateDefinedVersionAsync, cache.StoreDefinedVersion, t));
            exactVersion = new(
                t => ProcessOperation(cache.TryGetExactVersion, CalculateExactVersionAsync, cache.StoreExactVersion, t));
            allReleases = new(
                t => ProcessOperation(cache.TryGetAllReleases, CalculateAllReleasesAsync, cache.StoreAllReleases, t));
            lastRelease = new(
                t => ProcessOperation(cache.TryGetLastRelease, CalculateLastReleaseAsync, cache.StoreLastRelease, t));
            externalDependencies = new(
                t => ProcessOperation(cache.TryGetExternalDependencies, CalculateExternalDependenciesAsync, cache.StoreExternalDependencies, t));
        }

        protected delegate bool TryGetValueFromCache<TOutput>(IIdentifier itemKey, out TOutput value);

        public IItemRecord Record { get; }

        public IItem? Parent => parent.Value;

        protected Func<IItemRecord, IItem> ItemFactory { get; }

        public Task<string?> TryGetVersionFilePathAsync(CancellationToken cancellationToken = default)
            => versionFilePath.GetValueAsync(cancellationToken);

        public Task<SemVersion?> TryGetDefinedVersionAsync(CancellationToken cancellationToken = default)
            => definedVersion.GetValueAsync(cancellationToken);

        public Task<SemVersion> GetExactVersionAsync(CancellationToken cancellationToken = default)
            => exactVersion.GetValueAsync(cancellationToken);

        public Task<IRelease?> TryGetLastReleaseAsync(CancellationToken cancellationToken = default)
            => lastRelease.GetValueAsync(cancellationToken);

        public Task<IReadOnlyList<IRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default)
            => allReleases.GetValueAsync(cancellationToken);

        public Task<IReadOnlyCollection<IExternalDependency>> GetExternalDependenciesAsync(CancellationToken cancellationToken = default)
            => externalDependencies.GetValueAsync(cancellationToken);

        // TODO: [P3] refactor this method
        protected virtual async Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesAsync(CancellationToken cancellationToken)
        {
            var directory = Record.Path;
            string filePath = Path.Combine(directory, "Packages.props");
            var parentTask = Parent is null
                ? Task.FromResult<IReadOnlyCollection<IExternalDependency>>(Array.Empty<IExternalDependency>())
                : Parent.GetExternalDependenciesAsync(cancellationToken);

            if (!File.Exists(filePath))
            {
                return await parentTask;
            }

            var parentDependencies = await parentTask;
            var map = parentDependencies
                .ToDictionary(d => d.Name, d => d.Version);

            XDocument xContent = await XDocument.LoadAsync(File.OpenText(filePath), LoadOptions.None, cancellationToken);

            if (xContent.Root is null)
            {
                return await parentTask;
            }

            foreach (var xReference in xContent.Descendants(xContent.Root.GetDefaultNamespace() + "PackageReference"))
            {
                var name = xReference.Attribute("Update")?.Value;
                var stringVersion = xReference.Attribute("Version")?.Value;

                if (name is not null && stringVersion is not null
                                     && SemVersion.TryParse(stringVersion, out var version))
                {
                    map[name] = version;
                }
            }

            return map
                .Select(i => new ExternalDependency(i.Key, i.Value))
                .ToList();
        }

        protected async Task<TResult> ProcessOperation<TResult>(
            TryGetValueFromCache<TResult> tryGetFromCache,
            Func<CancellationToken, Task<TResult>> calculateOperation,
            Action<IIdentifier, TResult> storeInCache,
            CancellationToken cancellationToken)
        {
            if (tryGetFromCache(Record.Identifier, out var result))
            {
                return result;
            }

            result = await calculateOperation(cancellationToken);
            storeInCache(Record.Identifier, result);
            return result;
        }

        private async Task<string?> CalculateVersionFilePathAsync(CancellationToken cancellationToken)
        {
            var directory = Record.Path;
            string filePath = Path.Combine(directory, Constants.VERSION_FILE_NAME);

            return File.Exists(filePath)
                ? filePath
                : Parent is null
                    ? null
                    : await Parent.TryGetVersionFilePathAsync(cancellationToken);
        }

        // TODO: [P3] refactor this method
        private async Task<SemVersion?> CalculateDefinedVersionAsync(CancellationToken cancellationToken)
        {
            var directory = Record.Path;
            string filePath = Path.Combine(directory, Constants.VERSION_FILE_NAME);

            if (!File.Exists(filePath))
            {
                return Parent is null ? null : await Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            string? versionString = null;

            try
            {
                var options = new JsonDocumentOptions() { CommentHandling = JsonCommentHandling.Skip };
                var versionDocument = await JsonDocument.ParseAsync(File.OpenRead(filePath), options, cancellationToken);
                var rootElement = versionDocument.RootElement;
                versionString = rootElement
                    .GetProperty(Constants.VERSION_PROPERTY_NAME)
                    .GetString();
            }
            catch (JsonException e)
            {
                // TODO: logging
                Console.WriteLine($"Error in version.json: {e.Message}");
            }

            if (versionString is null
                || !SemVersion.TryParse(versionString, out SemVersion parsedVersion))
            {
                return Parent is null ? null : await Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            return parsedVersion;
        }

        private async Task<SemVersion> CalculateExactVersionAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private async Task<IReadOnlyList<IRelease>> CalculateAllReleasesAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private async Task<IRelease?> CalculateLastReleaseAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
