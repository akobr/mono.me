using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Semver;

namespace _42.Monorepo.Cli.Versioning
{
    public class PackageVersion
    {
        private static readonly Regex _fourSegmentVersionExpression = new Regex(
            @"(?<major>[0-9]+)\.(?<minor>[0-9]+)\.(?<patch>[0-9]+)(\.(?<fourth>[0-9]+))?(?<prerelease>(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?)(?<build>(?:\+[0-9A-Za-z-]+)?)",
            RegexOptions.Compiled);

        private readonly SemVersion _semVersion;
        private readonly int? _fourSegment;

        public PackageVersion(SemVersion version, int? fourSegment = null)
        {
            _semVersion = version;
            _fourSegment = fourSegment;
        }

        public static bool TryParse(string text, [MaybeNullWhen(false)] out PackageVersion version)
        {
            if (SemVersion.TryParse(text, out var semVersion))
            {
                version = new PackageVersion(semVersion);
                return true;
            }

            var match = _fourSegmentVersionExpression.Match(text);

            if (!match.Success)
            {
                version = null;
                return false;
            }

            var newVersionText = $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}.{match.Groups["patch"].Value}{match.Groups["prerelease"]}{match.Groups["build"]}";

            if (!SemVersion.TryParse(newVersionText, out semVersion)
                || !int.TryParse(match.Groups["fourth"].Value, out var fourNumber))
            {
                version = null;
                return false;
            }

            version = new PackageVersion(semVersion, fourNumber);
            return true;
        }
    }
}
