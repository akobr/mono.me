using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace _42.Monorepo.Repo.Generator
{
    internal static class RepositoryExtensions
    {
        public static void Commit(this IRepository @this, string message)
        {
            var gitConfig = @this.Config;
            var signature = gitConfig.BuildSignature(DateTimeOffset.Now);
            @this.Commit(message, signature, signature);
        }

        public static void Merge(this IRepository @this, Branch branch)
        {
            var gitConfig = @this.Config;
            var signature = gitConfig.BuildSignature(DateTimeOffset.Now);
            @this.Merge(branch, signature);
        }
    }
}
