namespace c0ded0c.Core
{
    public static class Constants
    {
        public const string CURRENT_DIRECTORY = ".";

        public const string SPARE_WORKING_DIRECTORY = "job";
        public const string SPARE_OUTPUT_DIRECTORY = "dist";
        public const string SPARE_RUN_NAME = "debug";

        public const string PACKAGE_EXTENSION = ".cdpkg";

        public const string INFO_KEY = "info";

        public const char DEFAULT_LIST_SEPARATOR = '|';
        public static readonly char[] LIST_SEPARATOR = new[] { DEFAULT_LIST_SEPARATOR, ';' };
    }
}
