using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.ConventionalCommits;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Git;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Operations;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Texo.Core.Markdown.Builder;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Semver;
using Sharprompt;

using Repository = LibGit2Sharp.Repository;

namespace _42.Monorepo.Cli.Commands.Release
{
    [Command(CommandNames.RELEASE, Description = "Create new release of a workstead or project.")]
    public class ReleaseCommand : BaseCommand
    {
        private readonly IGitRepositoryService _repositoryService;
        private readonly IGitHistoryService _historyService;
        private readonly ReleaseOptions _options;

        public ReleaseCommand(
            IExtendedConsole console,
            ICommandContext context,
            IGitRepositoryService repositoryService,
            IGitHistoryService historyService,
            IOptions<ReleaseOptions> configuration)
            : base(console, context)
        {
            _repositoryService = repositoryService;
            _historyService = historyService;
            _options = configuration.Value;
        }

        protected override async Task<int> ExecuteAsync()
        {
            var item = Context.Item;
            var originalItem = item;
            var record = item.Record;
            var originalRecord = record;
            var identifier = record.Identifier.Humanized;
            var originalIdentifier = record.Identifier.Humanized;
            var versionFilePath = await item.TryGetVersionFilePathAsync();
            var exactVersions = await item.GetExactVersionsAsync();

            if (string.IsNullOrEmpty(versionFilePath))
            {
                Console.WriteImportant("There is nothing releasable on current position of the mono-repository.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var versionFolder = Path.GetDirectoryName(versionFilePath);

            while (!record.Path.EqualsOrdinalIgnoreCase(versionFolder))
            {
                item = item.Parent;

                if (item == null)
                {
                    Console.WriteImportant("There is nothing releasable on current position of the mono-repository.");
                    return ExitCodes.WARNING_NO_WORK_NEEDED;
                }

                record = item.Record;
                identifier = record.Identifier.Humanized;
            }

            if (item != originalItem)
            {
                Console.WriteImportant(
                    $"{originalRecord.GetTypeAsString()} ",
                    originalIdentifier.ThemedHighlight(Console.Theme),
                    " is not releasable by its own");
                Console.WriteLine("It can be released as part of ".ThemedLowlight(Console.Theme), identifier.ThemedHighlight(Console.Theme));
                if (!Console.Confirm($"Do you want to proceed with release of {identifier}", true))
                {
                    return ExitCodes.WARNING_ABORTED;
                }

                Console.WriteLine();
            }

            Console.WriteHeader($"Release for {record.GetTypeAsString().ToLowerInvariant()} ", identifier.ThemedHighlight(Console.Theme));

            using var repo = _repositoryService.BuildRepository();
            Console.WriteLine("In branch ", repo.Head.FriendlyName.ThemedHighlight(Console.Theme));
            Console.WriteLine("The current version is ", exactVersions.PackageVersion.ToString().ThemedHighlight(Console.Theme));

            var isReleaseBranch = _options.Branches.Any(bt => Regex.IsMatch(repo.Head.CanonicalName, bt, RegexOptions.Singleline));

            if (isReleaseBranch)
            {
                Console.WriteLine();
                Console.WriteImportant("Current branch is not marked as releasable");
                if (!Console.Confirm("Do you want to proceed anyway", false))
                {
                    return ExitCodes.WARNING_ABORTED;
                }

                Console.WriteLine();
            }

            var releases = await item.GetAllReleasesAsync();
            var lastRelease = releases.Count > 0 ? releases[0] : null;
            var isFirstRelease = false;

            if (releases.Count > 1)
            {
                Console.WriteLine($"There is already {releases.Count} releases and the latest is {lastRelease?.Version}".ThemedLowlight(Console.Theme));
            }
            else if (releases.Count == 1)
            {
                Console.WriteLine($"There is just one release with version {lastRelease?.Version}".ThemedLowlight(Console.Theme));
            }
            else
            {
                isFirstRelease = true;
                Console.WriteLine("There is no release yet.".ThemedLowlight(Console.Theme));
            }

            Console.WriteLine();

            var releaseCommits = releases.Select(r => r.Tag.Target.Peel<Commit>()).ToList();
            var targetedRepoFolderPath = record.Type == RecordType.Repository
                ? Constants.SOURCE_DIRECTORY_NAME
                : record.Path.GetRelativePath(Context.Repository.Record.Path);
            var report = _historyService.GetHistory(targetedRepoFolderPath, releaseCommits);

            if (report.Changes.Count < 1
                && report.UnknownChanges.Count < 1)
            {
                Console.WriteImportant("No changes has been detected, the release is aborted.");
                return ExitCodes.WARNING_ABORTED;
            }

            var previousVersion = isFirstRelease
                ? exactVersions.PackageVersion
                : lastRelease?.Version ?? new SemVersion(1);
            var newVersion = exactVersions.PackageVersion;

            var majorChangeTypeSet = new HashSet<string>(_options.Changes.Major);
            var minorChangeTypeSet = new HashSet<string>(_options.Changes.Minor);
            var patchChangeTypeSet = new HashSet<string>(_options.Changes.Patch);
            var harmlessChangeTypeSet = new HashSet<string>(_options.Changes.Harmless);

            var majorChanges = report.Changes
                .Where(ch => ch.Message.IsBreakingChange || majorChangeTypeSet.Contains(ch.Message.Type))
                .Select(ch => ch.Message)
                .ToList();

            var minorChanges = report.Changes
                .Where(ch => minorChangeTypeSet.Contains(ch.Message.Type))
                .Select(ch => ch.Message)
                .ToList();

            var pathChanges = report.Changes
                .Where(ch => patchChangeTypeSet.Contains(ch.Message.Type))
                .Select(ch => ch.Message)
                .ToList();

            var harmlessChanges = report.Changes
                .Where(ch => harmlessChangeTypeSet.Contains(ch.Message.Type))
                .Select(ch => ch.Message)
                .ToList();

            if (isFirstRelease)
            {
                Console.WriteImportant("This is the first release.");
                newVersion = AskForVersion("What should be the first version", newVersion);
                Console.WriteLine();
            }
            else
            {
                var hasMajorChange = majorChanges.Count > 0;
                var hasMinorChange = minorChanges.Count > 0;
                var hasPatchChange = pathChanges.Count > 0;

                if (hasMajorChange)
                {
                    newVersion = new(previousVersion.Major + 1);
                }
                else if (hasMinorChange)
                {
                    newVersion = new(previousVersion.Major, previousVersion.Minor + 1);
                }
                else if (hasPatchChange)
                {
                    newVersion = new(previousVersion.Major, previousVersion.Minor, previousVersion.Patch + 1);
                }
                else
                {
                    Console.WriteImportant($"There are only unknown changes [{report.UnknownChanges.Count}]");
                    if (!Console.Confirm("Do you want to processed with the release"))
                    {
                        return ExitCodes.WARNING_ABORTED;
                    }

                    newVersion = AskForVersion("Please provide the version", newVersion);
                    Console.WriteLine();
                }
            }

            var preview = new ReleasePreview()
            {
                Version = newVersion,
                CurrentVersion = exactVersions.PackageVersion,
                PreviousRelease = lastRelease,
                Tag = $"{item.Record.Name}/v.{newVersion}",
                Branch = $"release/{item.Record.RepoRelativePath}/v.{newVersion}",
                NotesRepoPath = $"docs/Monorepo/Cli/releases/{newVersion}.md",
                MajorChanges = majorChanges,
                MinorChanges = minorChanges,
                PathChanges = pathChanges,
                HarmlessChanges = harmlessChanges,
                UnknownChanges = report.UnknownChanges,
            };

            if (record.Type != RecordType.Project)
            {
                var projects = ((ICompositionOfProjects)item).GetAllProjects();
                var listOfReleases = new List<(string Project, SemVersion Version)>();

                foreach (var project in projects)
                {
                    var projectVersionFile = await project.TryGetVersionFilePathAsync();
                    if (versionFilePath.EqualsOrdinalIgnoreCase(projectVersionFile))
                    {
                        continue;
                    }

                    var projectIdentifier = project.Record.Identifier.Humanized;
                    var projectVersion = (await project.GetExactVersionsAsync()).PackageVersion;
                    listOfReleases.Add((projectIdentifier, projectVersion));
                }

                preview.ProjectsToRelease = listOfReleases;
            }

            ShowReleasePreview(preview);

            var possibleUserOptions = new List<string> { "show release notes", "change version", "release it!", "abort" };
            if (report.UnknownChanges.Count > 0)
            {
                possibleUserOptions.Insert(0, "show unknown changes");
            }

            var userOptions = new SelectOptions<string>
            {
                Items = possibleUserOptions,
                Message = "Please pick what to do next",
                DefaultValue = "show release notes",
            };

            while (true)
            {
                Console.WriteLine();
                var userOption = Console.Select(userOptions);

                switch (userOption)
                {
                    case "show unknown changes":
                        ShowUnknownChanges(report.UnknownChanges);
                        break;

                    case "show release notes":
                        ShowReleaseNotes(preview);
                        break;

                    case "change version":
                        ChangeVersion(preview);
                        ShowReleasePreview(preview);
                        break;

                    case "release it!":
                        ProcessRelease(preview);
                        break;

                    default:
                        Console.WriteImportant("The release has been aborted.");
                        return ExitCodes.WARNING_ABORTED;
                }
            }
        }

        private static bool IsRelevantCommit(Repository repository, Commit commit, string repoFolderPath)
        {
            if (!commit.Parents.Any())
            {
                return commit.Tree[repoFolderPath] is not null;
            }

            return commit.Parents.Any(p => IsRelevantCommit(repository, commit, p, repoFolderPath));
        }

        private static bool IsRelevantCommit(Repository repository, Commit currentCommit, Commit oldCommit, string repoFolderPath)
        {
            var currentTree = currentCommit.Tree;
            var oldTree = oldCommit.Tree;

            var currentFolderEntry = currentTree[repoFolderPath];
            var oldFolderEntry = oldTree[repoFolderPath];

            if (currentFolderEntry is null)
            {
                return oldFolderEntry is not null;
            }

            if (oldFolderEntry is null)
            {
                return true;
            }

            var changes = repository.Diff.Compare<TreeChanges>(oldTree, currentTree, new[] { repoFolderPath });
            return changes.Count > 0;
        }

        private void ProcessRelease(ReleasePreview preview)
        {
            throw new NotImplementedException();
        }

        private void ChangeVersion(ReleasePreview preview)
        {
            Console.WriteLine();
            preview.Version = AskForVersion("Please provide new version", preview.Version);
            Console.WriteImportant("Version has been changed to: ", preview.Version.ToString().ThemedHighlight(Console.Theme));
            Console.WriteLine();
        }

        private void ShowReleaseNotes(ReleasePreview preview)
        {
            var breakChanges = preview.MajorChanges;
            var unknownChanges = preview.UnknownChanges;

            var features = preview.MinorChanges
                .Where(m => m.Type is "feat" or ":sparkles:")
                .ToList();

            var minorChanges = preview.MinorChanges
                .Where(m => m.Type is not "feat" or ":sparkles:")
                .Concat(preview.PathChanges)
                .ToList();

            var markdownBuilder = new MarkdownBuilder();
            markdownBuilder.Header($"Release v.{preview.Version}");
            markdownBuilder.Bullet($"Tagged as `{preview.Tag}`");
            markdownBuilder.Bullet($"At {DateTime.Today.ToShortDateString()} {DateTime.Now.ToLongTimeString()}");

            if (breakChanges.Count > 0)
            {
                markdownBuilder.Header("Breaking changes", 2);
                foreach (var change in breakChanges)
                {
                    markdownBuilder.Bullet($"{change.Type}: {change.Description}");
                }
            }

            if (features.Count > 0)
            {
                markdownBuilder.Header("New features", 2);
                foreach (var feature in features)
                {
                    markdownBuilder.Bullet(feature.Description);
                }
            }

            if (minorChanges.Count > 0)
            {
                markdownBuilder.Header("Minor changes", 2);
                foreach (var change in minorChanges)
                {
                    markdownBuilder.Bullet($"{change.Type}: {change.Description}");
                }
            }

            if (unknownChanges.Count > 0)
            {
                markdownBuilder.Header("Unknown changes", 2);
                foreach (var unknown in unknownChanges)
                {
                    markdownBuilder.Bullet($"{unknown.Sha[..4]}: {unknown.MessageShort}");
                }
            }

            Console.WriteLine();
            System.Console.WriteLine(markdownBuilder.ToString());
        }

        private void ShowReleasePreview(ReleasePreview preview)
        {
            Console.WriteHeader("Release preview");
            Console.WriteLine("Version: ", preview.Version.ToString().ThemedHighlight(Console.Theme));
            Console.WriteLine($"Tag:     {preview.Tag}");

            if (_options.CreateReleaseBranch)
            {
                Console.WriteLine($"Branch:  {preview.Branch}");
            }

            Console.WriteLine($"Notes:   {preview.NotesRepoPath}");

            var isNewVersion = preview.CurrentVersion != preview.Version;

            if (isNewVersion)
            {
                Console.WriteImportant("Version.json file needs to be updated as part of the release.");
            }

            if (preview.ProjectsToRelease.Count > 0)
            {
                Console.WriteLine();
                Console.WriteHeader("Released projects");
                var pRoot = new Composition(preview.Tag);
                pRoot.Children.AddRange(preview.ProjectsToRelease.Select(p => new Composition($"{p.Project}/v.{p.Version}")));
                Console.WriteTree(pRoot, n => n);
            }

            Console.WriteLine();
            Console.WriteHeader("Change list");
            var root = new Composition(preview.Version.ToString());
            root.Children.Add(BuildChangeNode("major (breaking changes)", preview.MajorChanges));
            root.Children.Add(BuildChangeNode("minor (new features)", preview.MinorChanges));
            root.Children.Add(BuildChangeNode("patch (code changes)", preview.PathChanges));

            if (preview.UnknownChanges.Count > 0)
            {
                root.Children.Add(new[] { "unknown ", $"(danger changes) [{preview.UnknownChanges.Count}]".Highlight() });
            }

            root.Children.Add(BuildChangeNode("harmless (other changes)", preview.HarmlessChanges));
            Console.WriteTree(root, s => s);
        }

        private void ShowUnknownChanges(IEnumerable<Commit> unknownCommits)
        {
            var count = 0;

            foreach (var commit in unknownCommits)
            {
                Console.WriteLine();
                Console.WriteLine(commit.MessageShort);
                Console.WriteLine(commit.Sha.ThemedLowlight(Console.Theme));

                if (++count > 20)
                {
                    Console.WriteLine("⁞");
                    break;
                }
            }
        }

        private static Composition BuildChangeNode(string changeTitle, IReadOnlyCollection<IConventionalMessage> affectedChanges)
        {
            var node = new Composition($"{changeTitle} [{affectedChanges.Count}]");
            var countMap = new Dictionary<string, int>();

            foreach (var change in affectedChanges)
            {
                countMap.TryGetValue(change.Type, out var count);
                countMap[change.Type] = count + 1;
            }

            foreach (var (key, value) in countMap)
            {
                node.Children.Add($"{key} [{value}]");
            }

            return node;
        }

        private SemVersion AskForVersion(string message, SemVersion newVersion)
        {
            var versionInput = Console.Input<string>(message, $"{newVersion}");

            if (!SemVersion.TryParse(versionInput, out var requestedVersion))
            {
                throw new InvalidOperationException("A version needs to be a valid semantic version.");
            }

            return requestedVersion;
        }
    }
}
