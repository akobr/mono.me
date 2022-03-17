using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class AllReleasesOpStrategy : IOpStrategy<IReadOnlyList<IRelease>>
    {
        private readonly IGitRepositoryService _gitRepositoryService;

        public AllReleasesOpStrategy(IGitRepositoryService gitRepositoryService)
        {
            _gitRepositoryService = gitRepositoryService;
        }

        public async Task<IReadOnlyList<IRelease>> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var parentReleases = item.Parent is null
                ? Array.Empty<IRelease>()
                : await item.Parent.GetAllReleasesAsync(cancellationToken);

            List<IRelease> allReleases = parentReleases
                .Where(r => r.SubReleases.Any(sr => sr.Target.Identifier == item.Record.Identifier))
                .Select(r => r.SubReleases.First(sr => sr.Target.Identifier == item.Record.Identifier))
                .ToList();

            allReleases.AddRange(await Task.Run(() => GetExactReleases(item, cancellationToken), cancellationToken));
            allReleases.Sort(new ReleaseVersionComparer());
            return allReleases;
        }

        private List<IRelease> GetExactReleases(IItem item, CancellationToken cancellationToken)
        {
            var hierarchicalName = item.Record.GetHierarchicalName();
            string releasePrefix = hierarchicalName is "."
                ? $"v."
                : $"{hierarchicalName}/v.";

            var repository = item.Record.TryGetConcreteItem(RecordType.Repository) ?? MonorepoDirectoryFunctions.GetMonoRepository();
            List<IRelease> exactReleases = new();
            using var gitRepo = _gitRepositoryService.BuildRepository();

            foreach (var tag in gitRepo.Tags)
            {
                if (tag.FriendlyName.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase)
                    && Release.TryParse(tag, repository, out var release))
                {
                    exactReleases.Add(release);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return exactReleases;
        }
    }
}
