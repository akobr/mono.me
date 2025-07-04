using System.Collections.Generic;
using _42.Monorepo.Cli.Model;
using Semver;

namespace _42.Monorepo.Cli.Operations
{
    public class ReleaseVersionComparer : IComparer<IRelease>
    {
        public int Compare(IRelease? a, IRelease? b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (ReferenceEquals(null, b))
            {
                return 1;
            }

            if (ReferenceEquals(null, a))
            {
                return -1;
            }

            return SemVersion.CompareSortOrder(b.Version, a.Version);
        }
    }
}
