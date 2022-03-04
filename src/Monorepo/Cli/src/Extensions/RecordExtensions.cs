using System.Collections.Generic;
using System.Linq;
using System.Text;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Extensions
{
    public static class RecordExtensions
    {
        public static string GetHierarchicalName(this IRecord @this)
        {
            string repoPath = @this.RepoRelativePath;

            return repoPath.StartsWith("src/")
                ? repoPath[4..]
                : repoPath;
        }
    }
}
