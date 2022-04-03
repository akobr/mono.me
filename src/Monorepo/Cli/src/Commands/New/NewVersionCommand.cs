using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Templates;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;
using Semver;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.VERSION, Description = "Create new version file.")]
    public class NewVersionCommand : BaseCommand
    {
        public NewVersionCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, Description = "A custom version to set.")]
        public string? Version { get; } = string.Empty;

        protected override async Task<int> ExecuteAsync()
        {
            using var repo = new Repository(Context.Repository.Record.Path);
            SemVersion currentVersion = await Context.Item.TryGetDefinedVersionAsync() ?? new SemVersion(0, 1);
            var versionFileFullPath = await Context.Item.TryGetVersionFilePathAsync() ?? Path.Combine(Context.Repository.Record.Path, Constants.VERSION_FILE_NAME);
            var versionFileRepoPath = versionFileFullPath.GetRelativePath(Context.Repository.Record.Path);

            var lastChangeInVersion = repo.Commits
                .QueryBy(versionFileRepoPath)
                .FirstOrDefault();

            var versionFolderPath = Path.GetDirectoryName(versionFileFullPath);
            if (Context.Item.Record.Path.EqualsOrdinalIgnoreCase(versionFolderPath))
            {
                Console.WriteImportant($"There is already a version file at the current location with version {currentVersion}.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var versionedRecord = MonorepoDirectoryFunctions.GetRecord(versionFileFullPath);

            Console.WriteLine(
                "Currently ",
                Context.Item.Record.Name.ThemedHighlight(Console.Theme),
                " is versionend as part of ",
                versionedRecord.Name.ThemedHighlight(Console.Theme),
                " and the version is ",
                currentVersion.ToString());

            if (!Console.Confirm($"Do you want to create a separated version file for {Context.Item.Record.Name}"))
            {
                return ExitCodes.SUCCESS;
            }

            SemVersion version;

            if (string.IsNullOrWhiteSpace(Version)
                || !SemVersion.TryParse(Version, out version))
            {
                var inputVersion = Console.Input<string>("What is the initial version", currentVersion.ToString());

                if (!SemVersion.TryParse(inputVersion, out version))
                {
                    version = new SemVersion(0, 1);
                }
            }

            var hierarchical = Console.Confirm("Should the version be hierarchical");

            // version.json
            var versionTemplate = new VersionJsonT4(new VersionJsonModel
            {
                Version = version.ToString(),
                IsHierarchical = hierarchical,
            });

            var versionFilePath = Path.Combine(Context.Item.Record.Path, FileNames.VersionJson);
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(versionFilePath, versionTemplate.TransformText());
#endif

            var versionRepoPath = Path.Combine(Context.Item.Record.RepoRelativePath, FileNames.VersionJson);
            Console.WriteLine($"new version file: {versionRepoPath}");
            return ExitCodes.SUCCESS;
        }
    }
}
