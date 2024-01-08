using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Git;
using _42.Monorepo.Cli.Operations;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;

namespace _42.Monorepo.Cli.Commands.Release
{
    [Command(CommandNames.TAG, Description = "Create new git tag after a release.")]
    public class TagCommand : BaseCommand
    {
        private readonly IGitRepositoryService _repositoryService;
        private readonly IGitHistoryService _historyService;
        private readonly ReleaseOptions _options;

        public TagCommand(
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

        protected override Task<int> ExecuteAsync()
        {
            var repo = _repositoryService.BuildRepository();
            var isReleaseBranch = _options.Branches.Any(bt => Regex.IsMatch(repo.Head.CanonicalName, bt, RegexOptions.Singleline));

            if (!isReleaseBranch)
            {
                Console.WriteImportant(
                    "The current branch is not releasable: ",
                    repo.Head.FriendlyName.ThemedHighlight(Console.Theme));
                return Task.FromResult(ExitCodes.ERROR_WRONG_PLACE);
            }

            var releaseCommit = _historyService.FindFirstCommit(repo, c => c.MessageShort.StartsWith("release:"));

            if (releaseCommit is null)
            {
                Console.WriteImportant($"No release commit has been found.");
                return Task.FromResult(ExitCodes.ERROR_WRONG_PLACE);
            }

            var releaseName = releaseCommit.MessageShort[19..].Trim();
            var releaseTag = repo.Tags.FirstOrDefault(
                t => t.FriendlyName.EqualsOrdinalIgnoreCase(releaseName));

            if (releaseTag is not null)
            {
                Console.WriteImportant(
                    $"There is nothing to tag, the last release is already tagged as '",
                    releaseName.ThemedHighlight(Console.Theme),
                    "'.");
                return Task.FromResult(ExitCodes.WARNING_NO_WORK_NEEDED);
            }

            if (releaseCommit.Id != repo.Head.Tip.Id)
            {
                Console.WriteLine("The release commit is not on the HEAD.");
                if (!Console.Confirm($"Do you want to tag the release commit {releaseCommit.Sha[0..7]} (y) or the current HEAD (n)"))
                {
                    releaseCommit = repo.Head.Tip;
                }

                Console.WriteLine("Description how to release with clean git history is available at https://bit.ly/36Lu13b.".ThemedLowlight(Console.Theme));
            }

#if !DEBUG || TESTING
            repo.Tags.Add(releaseName, releaseCommit);
#endif
            Console.WriteImportant($"The tag {releaseName} has been created at commit {releaseCommit.Sha[0..7]}.");
            return Task.FromResult(ExitCodes.SUCCESS);
        }
    }
}
