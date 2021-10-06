using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.ConventionalCommits;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Output;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;
using Semver;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command("version")]
    public class NewVersionCommand : BaseCommand
    {
        public NewVersionCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, Description = "A custom version to set.")]
        public string? Version { get; } = string.Empty;

        protected override async Task ExecuteAsync()
        {

            using var repo = new Repository(Context.Repository.Record.Path);
            SemVersion currentVersion = await Context.Item.TryGetDefinedVersionAsync() ?? new SemVersion(0, 1);
            var versionFile = await Context.Item.TryGetVersionFilePathAsync() ?? Path.Combine(Context.Repository.Record.Path, Constants.VERSION_FILE_NAME);
            versionFile = versionFile.GetRelativePath(Context.Repository.Record.Path);

            var lastChangeInVersion = repo.Commits
                .QueryBy(versionFile)
                .FirstOrDefault();

            if (lastChangeInVersion == null)
            {
                throw new InvalidOperationException("There is no version history in the repository.");
            }

            Stack<Commit> affectingCommits = new();

            foreach (var commit in repo.Head.Commits)
            {
                if (commit.Id == lastChangeInVersion.Commit.Id)
                {
                    break;
                }

                affectingCommits.Push(commit);
            }

            if (affectingCommits.Count < 1)
            {
                throw new InvalidOperationException($"There are no changes in current version '{currentVersion}'.");
            }

            List<IConventionalCommitMessage> changes = new(affectingCommits.Count);
            ConventionalCommitMessageParser parser = new();

            while (affectingCommits.Count > 0)
            {
                var commit = affectingCommits.Pop();
                if (parser.TryParseCommitMessage(commit.MessageShort, out var message))
                {
                    changes.Add(message!);
                }
            }

            var majorChange = changes.Any(m => m.IsBreakingChange);
            var minorChange = changes.Any(m => m.Type == "feat");
            var patchChange = changes.Any(m => m.Type != "chore" || m.Type != "test" || m.Type != "build");

            if (majorChange)
            {
                currentVersion = new(currentVersion.Major + 1);
            }
            else if (minorChange)
            {
                currentVersion = new(currentVersion.Major, currentVersion.Minor + 1);
            }
            else if (patchChange)
            {
                currentVersion = new(currentVersion.Major, currentVersion.Minor, currentVersion.Patch + 1);
            }
            else
            {
                throw new InvalidOperationException("There are no changes which should affect the version.");
            }

            Colorful.Console.WriteLine($"New version should be {currentVersion}");
            Colorful.Console.WriteLine();
            Colorful.Console.WriteLine("Changes:");

            foreach (var change in changes)
            {
                Console.WriteLine($" > {change.Type}: {change.Description}");
            }
        }
    }
}
