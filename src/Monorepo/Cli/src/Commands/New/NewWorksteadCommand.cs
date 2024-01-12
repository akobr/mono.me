using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Templates;
using Alba.CsConsoleFormat.Fluent;
using McMaster.Extensions.CommandLineUtils;
using Semver;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.WORKSTEAD, Description = "Create new workstead.")]
    public class NewWorksteadCommand : BaseSourceCommand
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFeatureProvider _featureProvider;

        public NewWorksteadCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context,
            IFeatureProvider featureProvider)
            : base(console, context)
        {
            _fileSystem = fileSystem;
            _featureProvider = featureProvider;
        }

        [Argument(0, Description = "Name of the workstead.")]
        public string? Name { get; } = string.Empty;

        protected override async Task<int> ExecuteAsync()
        {
            var targetItem = Context.Item;

            if (Context.Item.Record.Type > RecordType.Workstead)
            {
                Console.WriteLine("A workstead can not be created under a project.".DarkGray());

                if (!Console.Confirm("Do you want to create a new top level workstead, instead"))
                {
                    return ExitCodes.WARNING_ABORTED;
                }

                targetItem = Context.Repository;
            }

            var name = Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Console.Input<string>("Please give me a name for the workstead");

                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.WriteImportant("A name of the workstead needs to be specified.".ThemedError(Console.Theme));
                    return ExitCodes.ERROR_WRONG_INPUT;
                }
            }

            name = name.Trim().ToValidItemName();

            var path = targetItem.Record.Type == RecordType.Repository
                ? _fileSystem.Path.Combine(targetItem.Record.Path, Constants.SOURCE_DIRECTORY_NAME, name)
                : _fileSystem.Path.Combine(targetItem.Record.Path, name);

            if (_fileSystem.Directory.Exists(path))
            {
                Console.WriteImportant($"The workstead '{name}' already exists.".ThemedError(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            _fileSystem.Directory.CreateDirectory(path);

            // Directory.Build.proj
            var buildProjTemplate = new DirectoryBuildProjT4();
            var buildProjFilePath = _fileSystem.Path.Combine(path, FileNames.DirectoryBuildProj);
            await _fileSystem.File.WriteAllTextAsync(buildProjFilePath, buildProjTemplate.TransformText());

            Console.WriteImportant("The workstead '", name.ThemedHighlight(Console.Theme), "' has been created.");
            Console.WriteLine($"Path: {path}".ThemedLowlight(Console.Theme));

            if (_featureProvider.IsEnabled(FeatureNames.Packages))
            {
                Console.WriteLine("If the workstead is containing a lot of dependencies that are unique in the mono-repository is recommended to create a separated definition file.");

                if (Console.Confirm("Do you want to setup new Directory.Packages.props"))
                {
                    // Directory.Packages.props
                    var packagesTemplate = new DirectoryPackagesPropsT4();
                    var packagesFilePath = _fileSystem.Path.Combine(path, FileNames.DirectoryPackagesProps);
                    await _fileSystem.File.WriteAllTextAsync(packagesFilePath, packagesTemplate.TransformText());
                }
            }

            if (_featureProvider.IsEnabled(FeatureNames.GitVersion)
                && Console.Confirm("Do you want to prepare new version file"))
            {
                var inputVersion = Console.Input<string>("What is the initial version", Constants.DEFAULT_INITIAL_VERSION);
                var hierarchical = Console.Confirm("Should the version be hierarchical");

                if (!SemVersion.TryParse(inputVersion, out _))
                {
                    inputVersion = Constants.DEFAULT_INITIAL_VERSION;
                }

                // version.json
                var versionTemplate = new VersionJsonT4(new VersionJsonModel
                {
                    Version = inputVersion,
                    IsHierarchical = hierarchical,
                });

                var versionFilePath = _fileSystem.Path.Combine(path, FileNames.VersionJson);
                await _fileSystem.File.WriteAllTextAsync(versionFilePath, versionTemplate.TransformText());
            }

            return ExitCodes.SUCCESS;
        }
    }
}
