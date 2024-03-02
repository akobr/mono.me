using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Records;
using Microsoft.CSharp;

namespace _42.Monorepo.Cli.Extensions
{
    public static class RecordExtensions
    {
        private static readonly CSharpCodeProvider CodeProvider = new();

        public static string GetHierarchicalName(this IRecord @this)
        {
            var repoPath = @this.RepoRelativePath;

            return repoPath.StartsWith("src/")
                ? repoPath[4..]
                : repoPath;
        }

        public static string GetValidIdentifier(this IRecord @this)
        {
            return CodeProvider.IsValidIdentifier(@this.Name)
                ? @this.Name
                : $"_{@this.Name}";
        }

        public static string GetWorksteadName(this IRecord @this)
        {
            var target = @this;

            while (target?.Type is RecordType.Workstead or RecordType.Project)
            {
                target = target.Parent;
            }

            return target?.Type is RecordType.TopWorkstead
                ? target.Name
                : string.Empty;
        }
    }
}
