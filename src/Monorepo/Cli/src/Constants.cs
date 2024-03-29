namespace _42.Monorepo.Cli
{
    public static class Constants
    {
        public const string APPLICATION_CONFIG_JSON = "app.config.json";
        public const string MONOREPO_CONFIG_JSON = "mrepo.json";

        public const string VERSION_FILE_NAME = "version.json";
        public const string VERSION_PROPERTY_NAME = "version";

        public const string PACKAGES_FILE_NAME = "Directory.Packages.props";

        public const string MONOREPO_CONFIG_DIRECTORY_NAME = ".mrepo";
        public const string ROOT_REPO_IDENTIFIER = ".";
        public const string SOURCE_DIRECTORY_NAME = "src";
        public const string TEST_DIRECTORY_NAME = "test";
        public const string SCRIPTS_DIRECTORY_REPO_PATH = $"{MONOREPO_CONFIG_DIRECTORY_NAME}\\scripts";

        public const string DIRECTORY_SEPARATOR = "/";

        public const string DEFAULT_INITIAL_VERSION = "0.1-alpha";
    }
}
