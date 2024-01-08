using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Features;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Templates;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Semver;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.PROJECT, Description = "Create new .net project.")]
    public class NewProjectCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFeatureProvider _featureProvider;
        private readonly IItemFullOptionsProvider _itemOptionsProvider;
        private readonly MonoRepoOptions _repoOptions;

        public NewProjectCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context,
            IFeatureProvider featureProvider,
            IOptions<MonoRepoOptions> monoRepoOptions,
            IItemFullOptionsProvider itemOptionsProvider)
            : base(console, context)
        {
            _fileSystem = fileSystem;
            _featureProvider = featureProvider;
            _repoOptions = monoRepoOptions.Value;
            _itemOptionsProvider = itemOptionsProvider;
        }

        [Argument(0, Description = "Name of the project.")]
        public string? Name { get; set; } = string.Empty;

        protected override async Task<int> ExecuteAsync()
        {
            var workstead = Context.Item.TryGetConcreteItem(RecordType.Workstead);

            if (workstead == null)
            {
                var worksteads = Context.Repository.GetWorksteads().ToDictionary(w => w.Record.Name);
                var selectedWorkstead = Console.Select(new Sharprompt.SelectOptions<string>
                {
                    Items = worksteads.Keys,
                    Message = "Under which workstead",
                    PageSize = 20,
                });
                workstead = worksteads[selectedWorkstead];
            }

            var name = Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Console.Input<string>("Please give me a name for the project");

                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.WriteImportant("A name of the project needs to be specified.".ThemedError(Console.Theme));
                    return ExitCodes.ERROR_WRONG_INPUT;
                }
            }

            var worksteadName = GetAssemblyName(workstead);
            var worksteadHasName = !string.IsNullOrWhiteSpace(worksteadName);
            name = name.Trim();
            var projectOptions = _itemOptionsProvider.GetProjectOptions($"{workstead.Record.RepoRelativePath}/{name}");

            if (projectOptions.UseFullProjectName()
                && worksteadHasName)
            {
                name = $"{worksteadName}.{name}";
            }

            var path = _fileSystem.Path.Combine(workstead.Record.Path, name);

            if (_fileSystem.Directory.Exists(path))
            {
                Console.WriteImportant($"The project '{name}' already exists.".ThemedError(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            _fileSystem.Directory.CreateDirectory(path);
            _fileSystem.Directory.CreateDirectory(_fileSystem.Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME));

            var projectFilePath = _fileSystem.Path.Combine(path, Constants.SOURCE_DIRECTORY_NAME, FileNames.GetProjectFileName(name));
            var worksteadNamespace = GetNamespace(workstead);
            var projectTemplate = new ProjectCsprojT4(new ProjectCsprojModel
            {
                HasCustomName = !projectOptions.UseFullProjectName() && worksteadHasName,
                AssemblyName = !projectOptions.UseFullProjectName() && worksteadHasName
                    ? $"{worksteadName}.{name}"
                    : name,
                RootNamespace = !string.IsNullOrWhiteSpace(worksteadNamespace)
                    ? $"{worksteadNamespace}.{name.ToValidItemName()}"
                    : name.ToValidItemName(),
            });

            if (!_fileSystem.File.Exists(projectFilePath))
            {
                await _fileSystem.File.WriteAllTextAsync(projectFilePath, projectTemplate.TransformText());
            }

            var testTypes = new List<string>();
            var testType = "none";

            if (_featureProvider.IsEnabled(FeatureNames.TestsXunit))
            {
                testTypes.Add("xunit");
            }

            if (_featureProvider.IsEnabled(FeatureNames.TestsNunit))
            {
                testTypes.Add("nunit");
            }

            if (testTypes.Count > 0)
            {
                testTypes.Add("none");

                testType = Console.Select(new Sharprompt.SelectOptions<string>()
                {
                    DefaultValue = testTypes.First(),
                    Items = testTypes,
                    Message = "Which unit testing framework do you want to use",
                });
            }

            var projectTestFilePath = _fileSystem.Path.Combine(path, Constants.TEST_DIRECTORY_NAME, FileNames.GetTestProjectFileName(name));

            if (testType != "none")
            {
                var testProjectTemplate = new ProjectTestCsprojT4(_featureProvider, testType);
                _fileSystem.Directory.CreateDirectory(_fileSystem.Path.Combine(path, Constants.TEST_DIRECTORY_NAME));

                if (!_fileSystem.File.Exists(projectTestFilePath))
                {
                    await _fileSystem.File.WriteAllTextAsync(projectTestFilePath, testProjectTemplate.TransformText());
                }
            }

            if (_featureProvider.IsEnabled(FeatureNames.IacPulumi))
            {
                // TODO: [IaC] implement it
                Console.WriteLine("Support for IaC is coming soon!");
            }

            Console.WriteImportant("The project '", name.ThemedHighlight(Console.Theme), "' has been created.");
            Console.WriteLine($"Directory: {path}".ThemedLowlight(Console.Theme));
            Console.WriteLine($"Project: {projectFilePath}".ThemedLowlight(Console.Theme));

            if (testType != "none")
            {
                Console.WriteLine($"Test Project: {projectTestFilePath}".ThemedLowlight(Console.Theme));
            }

            if (_featureProvider.IsEnabled(FeatureNames.GitVersion)
                && Console.Confirm("Do you want to set a custom versioning for this project"))
            {
                var inputVersion = Console.Input<string>("What is the initial version", Constants.DEFAULT_INITIAL_VERSION);

                if (!SemVersion.TryParse(inputVersion, out _))
                {
                    inputVersion = Constants.DEFAULT_INITIAL_VERSION;
                }

                // version.json
                var versionTemplate = new VersionJsonT4(new VersionJsonModel
                {
                    Version = inputVersion,
                    IsHierarchical = false,
                });
                var versionFilePath = _fileSystem.Path.Combine(path, FileNames.VersionJson);
                await _fileSystem.File.WriteAllTextAsync(versionFilePath, versionTemplate.TransformText());
            }

            return ExitCodes.SUCCESS;
        }

        private string GetNamespace(IItem item)
        {
            var itemToProcess = item;
            var builder = new StringBuilder();

            do
            {
                var options = _itemOptionsProvider.GetWorksteadOptions(itemToProcess.Record.RepoRelativePath);

                if (!options.IsSuppressed())
                {
                    if (builder.Length > 0)
                    {
                        builder.Insert(0, '.');
                    }

                    builder.Insert(0, itemToProcess.Record.GetValidIdentifier());
                }

                itemToProcess = itemToProcess.Parent;
            }
            while (itemToProcess is IWorkstead);

            var prefix = _repoOptions.Prefix.ToValidItemName();

            if (!string.IsNullOrEmpty(prefix))
            {
                if (builder.Length > 0)
                {
                    builder.Insert(0, '.');
                }

                builder.Insert(0, prefix);
            }

            return builder.ToString();
        }

        private string GetAssemblyName(IItem item)
        {
            var itemToProcess = item;
            var builder = new StringBuilder();

            do
            {
                var options = _itemOptionsProvider.GetWorksteadOptions(itemToProcess.Record.RepoRelativePath);

                if (!options.IsSuppressed())
                {
                    if (builder.Length > 0)
                    {
                        builder.Insert(0, '.');
                    }

                    builder.Insert(0, itemToProcess.Record.Name);
                }

                itemToProcess = itemToProcess.Parent;
            }
            while (itemToProcess is IWorkstead);

            var prefix = (_repoOptions.Prefix ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(prefix))
            {
                if (builder.Length > 0)
                {
                    builder.Insert(0, '.');
                }

                builder.Insert(0, prefix);
            }

            return builder.ToString();
        }
    }
}
