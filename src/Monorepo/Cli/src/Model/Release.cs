using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _42.Monorepo.Cli.Model.Records;
using LibGit2Sharp;
using Semver;

namespace _42.Monorepo.Cli.Model
{
    public class Release : IRelease
    {
        private readonly List<IRelease> subReleases;

        public Release(SemVersion version, ObjectId commitId, IRecord target)
        {
            CommitId = commitId;
            Target = target;
            Version = version;
            subReleases = new List<IRelease>();
        }

        public ObjectId CommitId { get; }

        public IRecord Target { get; }

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

        public static bool TryParse(Tag tag, IRecord repository, out Release release)
        {
            var tagName = tag.FriendlyName;
            var commit = tag.Target.Peel<Commit>();

            if (string.IsNullOrWhiteSpace(tagName))
            {
                release = CreateEmpty(commit);
                return false;
            }

            if (!TryParseReleaseName(tagName, repository, out var target, out var version))
            {
                release = CreateEmpty(commit);
                return false;
            }

            release = new Release(version, commit.Id, target);
            TryParseAnnotation(release, tag, commit, repository);
            return true;
        }

        private static void TryParseAnnotation(Release release, Tag tag, Commit commit, IRecord root)
        {
            if (!tag.IsAnnotated)
            {
                return;
            }

            var annotation = tag.Annotation;

            if (annotation is null)
            {
                return;
            }

            var lines = annotation.Message.Split(new[] { "\r\n", "\n" }, 256, StringSplitOptions.RemoveEmptyEntries);

            release.AddSubReleases(
                lines.Select(l =>
                    {
                        var isValid = TryParseReleaseName(l, root, out var subTarget, out var subVersion);
                        return new { Release = new Release(subVersion, commit.Id, subTarget), IsValid = isValid };
                    })
                    .Where(pr => pr.IsValid)
                    .Select(pr => pr.Release));
        }

        private static bool TryParseReleaseName(string releaseName, IRecord rootRecord, out IRecord target, out SemVersion version)
        {
            string[] segments = releaseName.Split('/', 256, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 1)
            {
                target = rootRecord;
                return TryParseVersionSegment(segments[0], out version);
            }

            var path = Path.Combine(
                rootRecord.Path,
                Constants.SOURCE_DIRECTORY_NAME,
                Path.Combine(segments[Range.EndAt(^1)]));

            target = MonorepoDirectoryFunctions.GetRecord(path);
            return TryParseVersionSegment(segments[^1], out version)
                   && target is not InvalidRecord;
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

        private static Release CreateEmpty(Commit commit)
        {
            return new Release(new SemVersion(0), commit.Id, new InvalidRecord(string.Empty));
        }
    }
}
