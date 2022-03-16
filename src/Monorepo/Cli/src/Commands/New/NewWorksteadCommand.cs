using System;
using System.IO;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Templates;
using Alba.CsConsoleFormat.Fluent;
using McMaster.Extensions.CommandLineUtils;
using Semver;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.WORKSTEAD, Description = "Create new workstead.")]
    public class NewWorksteadCommand : BaseCommand
    {
        private readonly IFeatureProvider _featureProvider;

        public NewWorksteadCommand(
            IExtendedConsole console,
            ICommandContext context,
            IFeatureProvider featureProvider)
            : base(console, context)
        {
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
                ? Path.Combine(targetItem.Record.Path, Constants.SOURCE_DIRECTORY_NAME, name)
                : Path.Combine(targetItem.Record.Path, name);

            if (Directory.Exists(path))
            {
                Console.WriteImportant($"The workstead '{name}' already exists.".ThemedError(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
            }

#if !DEBUG || TESTING
            Directory.CreateDirectory(path);
#endif

            // Directory.Build.proj
            var buildProjTemplate = new DirectoryBuildProjT4();
            var buildProjFilePath = Path.Combine(path, FileNames.DirectoryBuildProj);
#if !DEBUG || TESTING
            await File.WriteAllTextAsync(buildProjFilePath, buildProjTemplate.TransformText());
#endif
            Console.WriteImportant("The workstead '", name.ThemedHighlight(Console.Theme), "' has been created.");
            Console.WriteLine($"Path: {path}".ThemedLowlight(Console.Theme));

            if (_featureProvider.IsEnabled(FeatureNames.Packages))
            {
                Console.WriteLine("If the workstead is containing a lot of dependecies which are unique in the mono-repository is recomended do create a separeated definition file.");

                if (Console.Confirm("Do you want to setup new Directory.Packages.props"))
                {
                    // Directory.Packages.props
                    var packagesTemplate = new DirectoryPackagesPropsT4();
                    var packagesFilePath = Path.Combine(path, FileNames.DirectoryPackagesProps);
#if !DEBUG || TESTING
                    await File.WriteAllTextAsync(packagesFilePath, packagesTemplate.TransformText());
#endif
                }
            }

            if (_featureProvider.IsEnabled(FeatureNames.GitVersion)
                && Console.Confirm("Do you want to prepare new version file"))
            {
                var inputVersion = Console.Input<string>("What is the initial version", "0.1");
                var hierarchical = Console.Confirm("Should the version be hierarchical");

                if (!SemVersion.TryParse(inputVersion, out var version))
                {
                    version = new SemVersion(0, 1);
                }

                // version.json
                var versionTemplate = new VersionJsonT4(new VersionJsonModel
                {
                    Version = version.ToString(),
                    IsHierarchical = hierarchical,
                });

                var versionFilePath = Path.Combine(path, FileNames.VersionJson);
#if !DEBUG || TESTING
                await File.WriteAllTextAsync(versionFilePath, versionTemplate.TransformText());
#endif
            }

            return ExitCodes.SUCCESS;
        }
    }
}
