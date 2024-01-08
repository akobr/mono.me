using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Release;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Git;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Versioning;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Semver;

namespace _42.Monorepo.Cli.Commands.Update
{
    [Command(CommandNames.VERSION, Description = "Update version of a current location based on git history.")]
    public class UpdateVersionCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;
        private readonly IGitHistoryService _historyService;
        private readonly ReleaseOptions _options;

        public UpdateVersionCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context,
            IGitHistoryService historyService,
            IOptions<ReleaseOptions> configuration)
            : base(console, context)
        {
            _fileSystem = fileSystem;
            _historyService = historyService;
            _options = configuration.Value;
        }

        [Argument(0, Description = "A custom version to set.")]
        public string? Version { get; } = string.Empty;

        // TODO: [P3] refactor this and use services
        protected override async Task<int> ExecuteAsync()
        {
            using var repo = new Repository(Context.Repository.Record.Path);
            var currentVersion = await Context.Item.TryGetDefinedVersionAsync() ?? new VersionTemplate(Constants.DEFAULT_INITIAL_VERSION);
            var versionFileFullPath = await Context.Item.TryGetVersionFilePathAsync() ?? _fileSystem.Path.Combine(Context.Repository.Record.Path, Constants.VERSION_FILE_NAME);
            var versionFileRepoPath = versionFileFullPath.GetRelativePath(Context.Repository.Record.Path);
            var newVersion = new VersionTemplate(currentVersion.Template);

            var lastChangeInVersion = repo.Commits
                .QueryBy(versionFileRepoPath, new CommitFilter { SortBy = CommitSortStrategies.Topological })
                .FirstOrDefault();

            if (lastChangeInVersion == null)
            {
                Console.WriteImportant("There is no version history in the current location of the mono-repository.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var report = _historyService.GetHistory(repo, Context.Item.Record.RepoRelativePath, new Commit[] { lastChangeInVersion.Commit });
            var hasChanges = report.Changes.Count > 0;

            if (hasChanges)
            {
                var majorChangeTypeSet = new HashSet<string>(_options.Changes.Major);
                var minorChangeTypeSet = new HashSet<string>(_options.Changes.Minor);
                var patchChangeTypeSet = new HashSet<string>(_options.Changes.Patch);

                var majorChanges = report.Changes.Where(ch => ch.Message.IsBreakingChange || majorChangeTypeSet.Contains(ch.Message.Type)).ToList();
                var minorChanges = report.Changes.Where(ch => minorChangeTypeSet.Contains(ch.Message.Type)).ToList();
                var pathChanges = report.Changes.Where(ch => patchChangeTypeSet.Contains(ch.Message.Type)).ToList();

                var hasMajorChange = majorChanges.Count > 0;
                var hasMinorChange = minorChanges.Count > 0;
                var hasPatchChange = pathChanges.Count > 0;
                var someChanges = hasMajorChange || hasMinorChange || hasPatchChange;

                if (hasMajorChange)
                {
                    var newMajor = currentVersion.Version.Major + 1;
                    newVersion = new($"{newMajor}.0");
                }
                else if (hasMinorChange)
                {
                    var newMinor = currentVersion.Version.Minor + 1;
                    newVersion = new(new SemVersion(currentVersion.Version.Major, newMinor));
                }
                else if (hasPatchChange)
                {
                    var newPatch = currentVersion.Version.Patch + 1;
                    newVersion = new(new SemVersion(currentVersion.Version.Major, currentVersion.Version.Minor, newPatch, currentVersion.Version.Prerelease));
                }

                if (someChanges)
                {
                    Console.WriteImportant("New version should be ", $"{newVersion.Template}".ThemedHighlight(Console.Theme));
                    Console.WriteLine();
                    Console.WriteHeader("Changes:");
                    var count = 0;

                    foreach (var change in report.Changes)
                    {
                        count++;
                        Console.WriteLine($"> {change.Message.GetFullRepresentation()}");

                        if (count >= 50)
                        {
                            Console.WriteLine("... and more".ThemedLowlight(Console.Theme));
                            break;
                        }
                    }
                }
                else
                {
                    Console.WriteImportant("There are no changes which should affect the version.");
                }
            }

            if (report.UnknownChanges.Count > 0)
            {
                Console.WriteLine();
                Console.WriteHeader("Unknown changes:");
                var count = 0;

                foreach (var unknownCommit in report.UnknownChanges)
                {
                    count++;
                    Console.WriteLine($"> {unknownCommit.MessageShort}");

                    if (count >= 50)
                    {
                        Console.WriteLine("... and more".ThemedLowlight(Console.Theme));
                        break;
                    }
                }
            }
            else if (!hasChanges)
            {
                Console.WriteImportant($"There are no changes in current version '{currentVersion.Template}'.");
            }

            Console.WriteLine();

            if (Console.Confirm($"Do you want to update '{versionFileRepoPath}' version file"))
            {
                newVersion = Console.AskForVersionTemplate("What is the final version", newVersion.Template);
                ReleaseHelper.UpdateVersionFile(newVersion.Template, versionFileFullPath, _fileSystem);
            }

            return ExitCodes.SUCCESS;
        }
    }
}
