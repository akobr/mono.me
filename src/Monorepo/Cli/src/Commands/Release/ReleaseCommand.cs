using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.ConventionalCommits;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Git;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Operations;
using _42.Monorepo.Cli.Versioning;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Semver;
using Sharprompt;

namespace _42.Monorepo.Cli.Commands.Release
{
    [Command(CommandNames.RELEASE, Description = "Create new release of a current location.")]
    [Subcommand(typeof(TagCommand))]
    public class ReleaseCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;
        private readonly IGitRepositoryService _repositoryService;
        private readonly IGitHistoryService _historyService;
        private readonly ReleaseOptions _options;

        public ReleaseCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context,
            IGitRepositoryService repositoryService,
            IGitHistoryService historyService,
            IOptions<ReleaseOptions> configuration)
            : base(console, context)
        {
            _fileSystem = fileSystem;
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

            var versionFolder = _fileSystem.Path.GetDirectoryName(versionFilePath);

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

            Console.WriteHeader($"Release for {record.GetTypeAsString().ToLowerInvariant()} ", record.Name.ThemedHighlight(Console.Theme));

            using var repo = _repositoryService.BuildRepository();
            Console.WriteLine("In branch ", repo.Head.FriendlyName.ThemedHighlight(Console.Theme));
            Console.WriteLine("The current version is ", exactVersions.PackageVersion.ToString().ThemedHighlight(Console.Theme));

            var isReleaseBranch = _options.Branches.Any(bt => Regex.IsMatch(repo.Head.CanonicalName, bt, RegexOptions.Singleline));

            if (!isReleaseBranch)
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

            var targetedRepoFolderPath = record.Type == RecordType.Repository
                ? Constants.SOURCE_DIRECTORY_NAME
                : record.Path.GetRelativePath(Context.Repository.Record.Path);
            var report = _historyService.GetHistory(targetedRepoFolderPath, releases.Select(r => r.CommitId));

            if (report.Changes.Count < 1
                && report.UnknownChanges.Count < 1)
            {
                Console.WriteImportant("No changes has been detected, the release is aborted.");
                return ExitCodes.WARNING_ABORTED;
            }

            var previousVersion = isFirstRelease
                ? exactVersions.PackageVersion
                : lastRelease?.Version ?? new SemVersion(0, 1);
            var newVersion = new VersionTemplate(previousVersion);

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
                newVersion = Console.AskForVersionTemplate("What should be the first version", Constants.DEFAULT_INITIAL_VERSION);
                Console.WriteLine();
            }
            else
            {
                var hasMajorChange = majorChanges.Count > 0;
                var hasMinorChange = minorChanges.Count > 0;
                var hasPatchChange = pathChanges.Count > 0;

                if (hasMajorChange)
                {
                    var newMajor = previousVersion.Major + 1;
                    newVersion = new($"{newMajor}.0");
                }
                else if (hasMinorChange)
                {
                    var newMinor = previousVersion.Minor + 1;
                    newVersion = new(new SemVersion(previousVersion.Major, newMinor));
                }
                else if (hasPatchChange)
                {
                    var newPatch = previousVersion.Patch + 1;
                    newVersion = new(new SemVersion(previousVersion.Major, previousVersion.Minor, newPatch, previousVersion.Prerelease));
                }
                else
                {
                    Console.WriteImportant($"There are only unknown changes [{report.UnknownChanges.Count}]");
                    if (!Console.Confirm("Do you want to processed with the release"))
                    {
                        return ExitCodes.WARNING_ABORTED;
                    }

                    newVersion = Console.AskForVersionTemplate("Please provide the version", newVersion.Template);
                    Console.WriteLine();
                }
            }

            var hierarchicalName = item.Record.GetHierarchicalName();

            var preview = new ReleasePreview
            {
                VersionDefinition = newVersion,
                CurrentVersion = exactVersions.PackageVersion,
                VersionFileFullPath = versionFilePath,
                PreviousRelease = lastRelease,
                Tag = hierarchicalName is "." ? $"v.{newVersion.Version}" : $"{hierarchicalName}/v.{newVersion.Version}",
                Branch = hierarchicalName is "." ? $"release/v.{newVersion.Version}" : $"release/{hierarchicalName}/v.{newVersion.Version}",
                NotesRepoPath = hierarchicalName is "." ? $"docs/release-notes/{newVersion.Version}.md" : $"docs/{hierarchicalName}/release-notes/{newVersion.Version}.md",
                MajorChanges = majorChanges,
                MinorChanges = minorChanges,
                PathChanges = pathChanges,
                HarmlessChanges = harmlessChanges,
                UnknownChanges = report.UnknownChanges,
            };

            if (record.Type != RecordType.Project)
            {
                var projects = ((ICompositionOfProjects)item).GetAllProjects().ToList();
                var listOfReleases = new List<(string Project, SemVersion Version)>();

                foreach (var project in projects)
                {
                    var projectVersionFile = await project.TryGetVersionFilePathAsync();
                    if (!versionFilePath.EqualsOrdinalIgnoreCase(projectVersionFile))
                    {
                        continue;
                    }

                    var projectIdentifier = project.Record.GetHierarchicalName();
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
                        ChangeVersion(preview, item);
                        ShowReleasePreview(preview);
                        break;

                    case "release it!":
                        await ProcessReleaseAsync(preview);
                        return ExitCodes.SUCCESS;

                    default:
                        Console.WriteImportant("The release has been aborted.");
                        return ExitCodes.WARNING_ABORTED;
                }
            }
        }

        private async Task ProcessReleaseAsync(ReleasePreview preview)
        {
            Console.WriteLine();
            var inBranch = Console.Confirm("Should I automatically prepare a commit and a new branch", true);
            var isNewVersion = preview.CurrentVersion != preview.VersionDefinition.Version;

            Console.WriteLine();

            if (isNewVersion)
            {
                ReleaseHelper.UpdateVersionFile(preview.VersionDefinition.Template, preview.VersionFileFullPath, _fileSystem);
                var versionFileRepoPath = preview.VersionFileFullPath.GetRelativePath(Context.Repository.Record.Path);
                Console.WriteLine($"Version: {versionFileRepoPath}");
            }

            var releaseNotes = ReleaseHelper.BuildReleaseNotes(preview);
            var releaseNotesFullPath = _fileSystem.Path.Combine(Context.Repository.Record.Path, preview.NotesRepoPath);
            var releaseNotesDirectory = _fileSystem.Path.GetDirectoryName(releaseNotesFullPath)!;
            _fileSystem.Directory.CreateDirectory(releaseNotesDirectory);
            await _fileSystem.File.WriteAllTextAsync(releaseNotesFullPath, releaseNotes.ToString());

            Console.WriteLine($"Notes:   {preview.NotesRepoPath}");

            if (inBranch)
            {
                if (PrepareInGit(preview))
                {
                    Console.WriteLine($"Branch:  {preview.Branch}");
                    Console.WriteLine("...the branch is ready and checked out.".ThemedLowlight(Console.Theme));
                }

                return;
            }

            Console.WriteLine();
            Console.WriteLine("Don't forget to create the tag after a merge:");
            Console.WriteLine($"    {preview.Tag}".ThemedHighlight(Console.Theme));
            Console.WriteLine();
        }

        private bool PrepareInGit(ReleasePreview preview)
        {
            var isNewVersion = preview.CurrentVersion != preview.VersionDefinition.Version;
            using var repository = _repositoryService.BuildRepository();
            var statuses = repository.RetrieveStatus();

            if (statuses.Staged.Any())
            {
                Console.WriteImportant("There are already files in the git stage, please do the changes manually.");
                return false;
            }

#if !DEBUG || TESTING
            var branch = repository.CreateBranch(preview.Branch);
            LibGit2Sharp.Commands.Checkout(repository, branch);

            if (isNewVersion)
            {
                var versionFileRepoPath = preview.VersionFileFullPath.GetRelativePath(Context.Repository.Record.Path);
                LibGit2Sharp.Commands.Stage(repository, versionFileRepoPath);
            }

            LibGit2Sharp.Commands.Stage(repository, preview.NotesRepoPath);

            var gitConfig = repository.Config;
            var signature = gitConfig.BuildSignature(DateTimeOffset.Now);
            repository.Commit($"release: release of {preview.Tag}", signature, signature);
#endif
            return true;
        }

        private void ChangeVersion(ReleasePreview preview, IItem item)
        {
            var newVersion = Console.AskForVersionTemplate("Please provide new version", preview.VersionDefinition.Template);
            var hierarchicalName = item.Record.GetHierarchicalName();

            preview.VersionDefinition = newVersion;
            preview.Tag = hierarchicalName is "." ? $"v.{newVersion.Version}" : $"{hierarchicalName}/v.{newVersion.Version}";
            preview.Branch = hierarchicalName is "." ? $"release/v.{newVersion.Version}" : $"release/{hierarchicalName}/v.{newVersion.Version}";
            preview.NotesRepoPath = hierarchicalName is "." ? $"docs/release-notes/{newVersion.Version}.md" : $"docs/{hierarchicalName}/release-notes/{newVersion.Version}.md";

            Console.WriteLine();
            Console.WriteImportant("Version has been changed to: ", preview.VersionDefinition.Template.ThemedHighlight(Console.Theme));
            Console.WriteLine();
        }

        private void ShowReleaseNotes(ReleasePreview preview)
        {
            var markdownBuilder = ReleaseHelper.BuildReleaseNotes(preview);

            Console.WriteLine();
            System.Console.WriteLine(markdownBuilder.ToString());
        }

        private void ShowReleasePreview(ReleasePreview preview)
        {
            Console.WriteHeader("Release preview");
            Console.WriteLine(
                "Version: ",
                preview.VersionDefinition.Template.ThemedHighlight(Console.Theme),
                $" ({preview.VersionDefinition.Version})".ThemedLowlight(Console.Theme));
            Console.WriteLine($"Tag:     {preview.Tag}");

            if (_options.CreateReleaseBranch)
            {
                Console.WriteLine($"Branch:  {preview.Branch}");
            }

            Console.WriteLine($"Notes:   {preview.NotesRepoPath}");

            var isNewVersion = preview.CurrentVersion != preview.VersionDefinition.Version;

            if (isNewVersion)
            {
                Console.WriteImportant("Version.json file needs to be updated as part of the release.");
            }

            if (preview.ProjectsToRelease.Count > 0)
            {
                Console.WriteLine();
                Console.WriteHeader("Released projects");
                var pRoot = new Composition(preview.Tag);

                if (isNewVersion)
                {
                    pRoot.Children.AddRange(preview.ProjectsToRelease.Select(p => new Composition($"{p.Project}/v.{preview.VersionDefinition.Version}")));
                }
                else
                {
                    pRoot.Children.AddRange(preview.ProjectsToRelease.Select(p => new Composition($"{p.Project}/v.{p.Version}")));
                }

                Console.WriteTree(pRoot, n => n);
            }

            Console.WriteLine();
            Console.WriteHeader("Change list");
            var root = new Composition(preview.VersionDefinition.Version.ToString());
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
    }
}
