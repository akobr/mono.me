using _42.Monorepo.Cli.Extensions;

namespace _42.Monorepo.Cli.Templates
{
    public static class FileNames
    {
        public const string DirectoryBuildProj = "Directory.Build.proj";
        public const string DirectoryBuildProps = "Directory.Build.props";
        public const string DirectoryBuildTargets = "Directory.Build.targets";
        public const string DirectoryPackagesProps = "Directory.Packages.props";

        public const string VersionJson = "version.json";
        public const string GlogalJson = "global.json";
        public const string MrepoJson = "mrepo.json";

        public static string GetProjectFileName(string projectName)
        {
            return $"{projectName.ToValidItemName()}.csproj";
        }

        public static string GetTestProjectFileName(string projectName)
        {
            return $"{projectName.ToValidItemName()}.Tests.csproj";
        }
    }
}
