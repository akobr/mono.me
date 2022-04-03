using System;
using System.Collections.Generic;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Git
{
    public interface IGitHistoryService
    {
        GitHistoryReport GetHistory(string targetedRepoRelativePath, IEnumerable<ObjectId> commitsToStop);

        GitHistoryReport GetHistory(Repository repo, string targetedRepoRelativePath, IEnumerable<Commit> commitsToStop);

        Commit? FindFirstCommit(Repository repo, Func<Commit, bool> predicate, int maxVisits = 200);
    }
}
