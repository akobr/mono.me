using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Templates;
using _42.Monorepo.Cli.Versioning;
using LibGit2Sharp;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.VERSION, Description = "Create new version file.")]
    public class NewVersionCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;

        public NewVersionCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context)
            : base(console, context)
        {
            _fileSystem = fileSystem;
        }

        [Argument(0, Description = "A custom version to set.")]
        public string? Version { get; } = string.Empty;

        protected override async Task<int> ExecuteAsync()
        {
            using var repo = new Repository(Context.Repository.Record.Path);
            var currentVersion = await Context.Item.TryGetDefinedVersionAsync() ?? new VersionTemplate(Constants.DEFAULT_INITIAL_VERSION);
            var versionFileFullPath = await Context.Item.TryGetVersionFilePathAsync() ?? _fileSystem.Path.Combine(Context.Repository.Record.Path, Constants.VERSION_FILE_NAME);

            var versionFolderPath = _fileSystem.Path.GetDirectoryName(versionFileFullPath);
            if (Context.Item.Record.Path.EqualsOrdinalIgnoreCase(versionFolderPath))
            {
                Console.WriteImportant($"There is already a version file at the current location with version {currentVersion}.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var versionedRecord = MonorepoDirectoryFunctions.GetRecord(versionFileFullPath);

            Console.WriteLine(
                "Currently ",
                Context.Item.Record.Name.ThemedHighlight(Console.Theme),
                " is versioned as part of ",
                versionedRecord.Name.ThemedHighlight(Console.Theme),
                " and the version is ",
                currentVersion.Template);

            if (!Console.Confirm($"Do you want to create a separated version file for {Context.Item.Record.Name}"))
            {
                return ExitCodes.SUCCESS;
            }

            var inputVersion = new VersionTemplate(Constants.DEFAULT_INITIAL_VERSION);

            if (string.IsNullOrWhiteSpace(Version)
                || !VersionTemplate.TryParse(Version, out _))
            {
                inputVersion = Console.AskForVersionTemplate("What is the initial version", currentVersion.Template);
            }

            var hierarchical = Console.Confirm("Should the version be hierarchical");

            // version.json
            var versionTemplate = new VersionJsonT4(new VersionJsonModel
            {
                Version = inputVersion.Template,
                IsHierarchical = hierarchical,
            });

            var versionFilePath = _fileSystem.Path.Combine(Context.Item.Record.Path, FileNames.VersionJson);
            await _fileSystem.File.WriteAllTextAsync(versionFilePath, versionTemplate.TransformText());

            var versionRepoPath = _fileSystem.Path.Combine(Context.Item.Record.RepoRelativePath, FileNames.VersionJson);
            Console.WriteLine($"new version file: {versionRepoPath}");
            return ExitCodes.SUCCESS;
        }
    }
}
