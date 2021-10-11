using System.Linq;

namespace _42.Monorepo.Cli.Configuration
{
    public static class ItemOperationsExtensions
    {
        public static bool IsVersioned(this ItemOptions options)
        {
            return !options.Exclude.Contains(Excludes.VERSION);
        }

        public static bool IsReleasable(this ItemOptions options)
        {
            return !options.Exclude.Contains(Excludes.RELEASE);
        }
    }
}
