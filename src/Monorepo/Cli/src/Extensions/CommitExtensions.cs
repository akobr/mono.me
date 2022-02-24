using System.Linq;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Extensions
{
    public static class CommitExtensions
    {
        public static bool IsRelevantCommit(this Commit commit, Repository repository, string repoRelativePath)
        {
            if (!commit.Parents.Any())
            {
                return commit.Tree[repoRelativePath] is not null;
            }

            return commit.Parents.Any(p => IsRelevantCommit(repository, commit, p, repoRelativePath));
        }

        private static bool IsRelevantCommit(Repository repository, Commit currentCommit, Commit oldCommit, string repoRelativePath)
        {
            var currentTree = currentCommit.Tree;
            var oldTree = oldCommit.Tree;

            var currentFolderEntry = currentTree[repoRelativePath];
            var oldFolderEntry = oldTree[repoRelativePath];

            if (currentFolderEntry is null)
            {
                return oldFolderEntry is not null;
            }

            if (oldFolderEntry is null)
            {
                return true;
            }

            var changes = repository.Diff.Compare<TreeChanges>(oldTree, currentTree, new[] { repoRelativePath });
            return changes.Count > 0;
        }
    }
}
