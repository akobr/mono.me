using System.Linq;

namespace _42.Monorepo.Cli.Configuration;

public static class BasicOptionsExtensions
{
    public static bool IsSuppressed(this IBasicOptions @this)
    {
        return @this.Custom.TryGetValue(CustomProperties.IS_SUPPRESSED, out var flagValue) &&
               bool.TryParse(flagValue, out var flag) && flag;
    }

    public static bool UseFullProjectName(this IBasicOptions @this)
    {
        return @this.Custom.TryGetValue(CustomProperties.USE_FULL_PROJECT_NAME, out var flagValue) &&
               bool.TryParse(flagValue, out var flag) && flag;
    }

    public static string GetFilePattern(this IBasicOptions @this)
    {
        @this.Custom.TryGetValue(CustomProperties.FILE_PATTERN, out var filePattern);
        return filePattern ?? string.Empty;
    }

    public static bool IsVersioned(this IBasicOptions options)
    {
        return !options.Exclude.Contains(Excludes.VERSION);
    }

    public static bool IsReleasable(this IBasicOptions options)
    {
        return !options.Exclude.Contains(Excludes.RELEASE);
    }
}
