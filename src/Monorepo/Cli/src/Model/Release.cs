using System;
using System.IO;
using LibGit2Sharp;
using Semver;
using System.Collections.Generic;
using System.Linq;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model
{
    public class Release : IRelease
    {
        private readonly List<IRelease> subReleases;

        public Release(SemVersion version, Tag tag, IItemRecord target)
        {
            Tag = tag;
            Target = target;
            Version = version;
            subReleases = new List<IRelease>();
        }

        public Tag Tag { get; }

        public IItemRecord Target { get; }

        public SemVersion Version { get; }

        public IReadOnlyCollection<IRelease> SubReleases => subReleases;

        public void AddSubRelease(IRelease release)
        {
            subReleases.Add(release);
        }

        public void AddSubReleases(IEnumerable<IRelease> releases)
        {
            subReleases.AddRange(releases);
        }

        public static bool TryParse(Tag tag, RepositoryRecord record, out Release release)
        {
            string tagName = tag.FriendlyName;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                release = CreateEmpty(tag);
                return false;
            }

            if (!TryParseReleaseName(tagName, record, out var target, out var version))
            {
                release = CreateEmpty(tag);
                return false;
            }

            release = new Release(version, tag, target);
            TryParseAnnotation(release, record);
            return true;
        }

        private static void TryParseAnnotation(Release release, IItemRecord rootItem)
        {
            var annotation = release.Tag.Annotation;

            if (annotation is null)
            {
                return;
            }

            string[] lines = annotation.Message.Split(new[] { "\r\n", "\n" }, 256, StringSplitOptions.RemoveEmptyEntries);

            release.AddSubReleases(
                lines.Select(l =>
                    {
                        var isValid = TryParseReleaseName(l, rootItem, out var subTarget, out var subVersion);
                        return new { Release = new Release(subVersion, release.Tag, subTarget), IsValid = isValid };
                    })
                    .Where(pr => pr.IsValid)
                    .Select(pr => pr.Release));
        }

        private static bool TryParseReleaseName(string releaseName, IItemRecord rootName, out IItemRecord target, out SemVersion version)
        {
            string[] segments = releaseName.Split('/', 256, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 1)
            {
                target = rootName;
                return TryParseVersionSegment(segments[^1], out version);
            }
            else
            {
                string path = Path.Combine(rootName.Path, Path.Combine(segments[Range.EndAt(^2)]));
                target = MonorepoDirectoryFunctions.GetItem(path);
                return TryParseVersionSegment(segments[^1], out version)
                       && target is not InvalidItemRecord;
            }
        }

        private static bool TryParseVersionSegment(string segment, out SemVersion version)
        {
            if (segment.StartsWith("v.", StringComparison.OrdinalIgnoreCase))
            {
                return SemVersion.TryParse(segment[2..], out version);
            }

            version = new SemVersion(0);
            return false;
        }

        private static Release CreateEmpty(Tag tag)
        {
            return new Release(new SemVersion(0), tag, new InvalidItemRecord(string.Empty));
        }
    }
}
