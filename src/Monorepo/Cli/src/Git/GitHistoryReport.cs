using System.Collections.Generic;
using _42.Monorepo.Cli.ConventionalCommits;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Git
{

    public class GitHistoryReport
    {
        public GitHistoryReport(
            IReadOnlyList<(Commit Commit, IConventionalMessage Message)> changes,
            IReadOnlyList<Commit> unknownChanges)
        {
            Changes = changes;
            UnknownChanges = unknownChanges;
        }

        public IReadOnlyList<(Commit Commit, IConventionalMessage Message)> Changes { get; }

        public IReadOnlyList<Commit> UnknownChanges { get; }
    }

}
