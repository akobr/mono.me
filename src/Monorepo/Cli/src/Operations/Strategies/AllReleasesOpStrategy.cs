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
        private readonly ITagsProvider tagsProvider;

        public AllReleasesOpStrategy(ITagsProvider tagsProvider)
        {
            this.tagsProvider = tagsProvider;
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
            string releasePrefix = $"{item.Record.Identifier.Humanized}/v.";
            var repository = item.Record.TryGetConcreteItem(ItemType.Repository) ?? MonorepoDirectoryFunctions.GetMonoRepository();

            List<IRelease> exactReleases = new();

            foreach (var tag in tagsProvider.GetTags())
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
