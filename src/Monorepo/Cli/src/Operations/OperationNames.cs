using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    public static class OperationNames
    {
        // Global operations
        public const string VERSION_FILE_PATH = nameof(IItem.TryGetVersionFilePathAsync);
        public const string PACKAGES_FILE_PATH = nameof(IItem.TryGetPackagesFilePathAsync);
        public const string DEFINED_VERSION = nameof(IItem.TryGetDefinedVersionAsync);
        public const string EXACT_VERSIONS = nameof(IItem.GetExactVersionsAsync);
        public const string LAST_RELEASE = nameof(IItem.TryGetLastReleaseAsync);
        public const string ALL_RELEASES = nameof(IItem.GetAllReleasesAsync);
        public const string EXTERNAL_DEPENDENCIES = nameof(IItem.GetExternalDependenciesAsync);

        // Project operations
        public const string INTERNAL_DEPENDENCIES = nameof(IProject.GetInternalDependenciesAsync);
        public const string IS_PACKABLE = nameof(IProject.GetIsPackableAsync);
        public const string PACKAGE_NAME = nameof(IProject.GetPackageNameAsync);
    }
}
