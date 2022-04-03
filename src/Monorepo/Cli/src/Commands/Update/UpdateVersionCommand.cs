using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Release;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Git;
using _42.Monorepo.Cli.Output;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Semver;

namespace _42.Monorepo.Cli.Commands.Update
{
    [Command(CommandNames.VERSION, Description = "Update version of a current location based on git history.")]
    public class UpdateVersionCommand : BaseCommand
    {
        private readonly IGitHistoryService _historyService;
        private readonly ReleaseOptions _options;

        public UpdateVersionCommand(
            IExtendedConsole console,
            ICommandContext context,
            IGitHistoryService historyService,
            IOptions<ReleaseOptions> configuration)
            : base(console, context)
        {
            _historyService = historyService;
            _options = configuration.Value;
        }

        [Argument(0, Description = "A custom version to set.")]
        public string? Version { get; } = string.Empty;

        // TODO: [P3] refactor this and use services
        protected override async Task<int> ExecuteAsync()
        {
            using var repo = new Repository(Context.Repository.Record.Path);
            SemVersion currentVersion = await Context.Item.TryGetDefinedVersionAsync() ?? new SemVersion(0, 1);
            var versionFileFullPath = await Context.Item.TryGetVersionFilePathAsync() ?? Path.Combine(Context.Repository.Record.Path, Constants.VERSION_FILE_NAME);
            var versionFileRepoPath = versionFileFullPath.GetRelativePath(Context.Repository.Record.Path);

            var lastChangeInVersion = repo.Commits
                .QueryBy(versionFileRepoPath)
                .FirstOrDefault();

            if (lastChangeInVersion == null)
            {
                Console.WriteImportant("There is no version history in the current location of the mono-repository.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var report = _historyService.GetHistory(repo, Context.Item.Record.RepoRelativePath, new Commit[] { lastChangeInVersion.Commit });
            var hasChanges = report.Changes.Count > 0;

            if (report.Changes.Count < 1 && report.UnknownChanges.Count < 1)
            {
                Console.WriteImportant($"There are no changes in current version '{currentVersion}'.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var majorChangeTypeSet = new HashSet<string>(_options.Changes.Major);
            var minorChangeTypeSet = new HashSet<string>(_options.Changes.Minor);
            var patchChangeTypeSet = new HashSet<string>(_options.Changes.Patch);
            var harmlessChangeTypeSet = new HashSet<string>(_options.Changes.Harmless);

            var majorChanges = report.Changes.Where(ch => ch.Message.IsBreakingChange || majorChangeTypeSet.Contains(ch.Message.Type)).ToList();
            var minorChanges = report.Changes.Where(ch => minorChangeTypeSet.Contains(ch.Message.Type)).ToList();
            var pathChanges = report.Changes.Where(ch => patchChangeTypeSet.Contains(ch.Message.Type)).ToList();
            var harmlessChanges = report.Changes.Where(ch => harmlessChangeTypeSet.Contains(ch.Message.Type)).ToList();

            var hasMajorChange = majorChanges.Count > 0;
            var hasMinorChange = minorChanges.Count > 0;
            var hasPatchChange = pathChanges.Count > 0;

            var newVersion = currentVersion.Change(prerelease: string.Empty);

            if (hasMajorChange)
            {
                newVersion = new(currentVersion.Major + 1);
            }
            else if (hasMinorChange)
            {
                newVersion = new(currentVersion.Major, currentVersion.Minor + 1);
            }
            else if (hasPatchChange)
            {
                newVersion = new(currentVersion.Major, currentVersion.Minor, currentVersion.Patch + 1);
            }
            else
            {
                Console.WriteImportant("There are no changes which should affect the version.");
            }

            if (hasChanges)
            {
                Console.WriteImportant($"New version should be ", $"{newVersion}".ThemedHighlight(Console.Theme));
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

            Console.WriteLine();

            if (Console.Confirm($"Do you want to update '{versionFileRepoPath}' version file"))
            {
                newVersion = Console.AskForVersion("What is the final version", newVersion);
#if !DEBUG || TESTING
                ReleaseHelper.UpdateVersionFile(newVersion.ToString(), versionFileFullPath);
#endif
            }

            return ExitCodes.SUCCESS;
        }
    }
}
