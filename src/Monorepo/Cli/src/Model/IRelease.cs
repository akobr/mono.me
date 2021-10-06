using System.Collections.Generic;
using _42.Monorepo.Cli.Model.Records;
using LibGit2Sharp;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public interface IRelease
    {
        Tag Tag { get; }

        IRecord Target { get; }

        SemVersion Version { get; }

        IReadOnlyCollection<IRelease> SubReleases { get; }
    }
}
