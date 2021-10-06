using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class LastReleaseOpStrategy : IOpStrategy<IRelease?>
    {
        public async Task<IRelease?> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var allReleases = await item.GetAllReleasesAsync(cancellationToken);
            return allReleases.Count > 0 ? allReleases[0] : null;
        }
    }
}
