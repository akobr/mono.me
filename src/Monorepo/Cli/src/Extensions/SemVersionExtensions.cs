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
            if (!int.TryParse(@this.Metadata, NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision))
            {
                revision = -1;
            }

            return new Version((int)@this.Major, (int)@this.Minor, (int)@this.Patch, revision);
        }

        public static SemVersion ToSemVersion(this Version @this)
        {
            var patch = @this.Build > 0 ? @this.Build : 0;
            var metadata = @this.Revision > 0
                ? new string[] { @this.Revision.ToString(CultureInfo.InvariantCulture) }
                : null;

            return new SemVersion(@this.Major, @this.Minor, patch, metadata: metadata);
        }
    }
}
