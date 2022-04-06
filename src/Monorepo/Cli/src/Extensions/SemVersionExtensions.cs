using Semver;

namespace _42.Monorepo.Cli.Extensions
{
    public static class SemVersionExtensions
    {
        public static string ToTemplate(this SemVersion @this)
        {
            return string.IsNullOrEmpty(@this.Prerelease)
                ? $"{@this.Major}.{@this.Minor}"
                : $"{@this.Major}.{@this.Minor}-{@this.Prerelease}";
        }
    }
}
