using System;
using System.Globalization;
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

        public static Version ToVersion(this SemVersion @this)
        {
            int.TryParse(@this.Build, NumberStyles.Integer, CultureInfo.InvariantCulture, out var build);
            return new Version(@this.Major, @this.Minor, @this.Patch, build);
        }

        public static SemVersion ToSemVersion(this Version @this)
        {
            var build = @this.Revision > 0
                ? @this.Revision.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            return new SemVersion(@this.Major, @this.Minor, @this.Build, build: build);
        }
    }
}
