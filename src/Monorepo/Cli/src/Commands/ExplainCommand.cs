using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Output;
using _42.Monorepo.Cli.Templates;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.EXPLAIN, Description = "Display explanation of an item inside the mono-repository.")]
    public class ExplainCommand : BaseCommand
    {
        private readonly Dictionary<string, Action> _explanationMapping;

        public ExplainCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            _explanationMapping = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
            InitialiseExplanations();
        }

        [Argument(0, "item-path", Description = "Relative path to a file/directory in the mono-repository.")]
        public string? RelativeItemPath { get; set; } = string.Empty;

        protected override Task<int> ExecuteAsync()
        {
            string name = (System.IO.Path.GetFileName(RelativeItemPath) ?? RelativeItemPath ?? string.Empty).ToLowerInvariant();

            if (_explanationMapping.TryGetValue(name, out var explanation))
            {
                explanation();
                return Task.FromResult(ExitCodes.SUCCESS);
            }

            switch (Context.Item.Record.Type)
            {
                case Model.RecordType.Repository:
                    Console.WriteLine("If you want to know more about a specific item, just execute:");
                    Console.WriteLine(
                        "    ",
                        "mrepo explain".ThemedHighlight(Console.Theme),
                        " <path-to-file-or-directory>".ThemedLowlight(Console.Theme));
                    break;

                default:
                    Console.WriteLine($"Currently you are in {Context.Item.Record.GetTypeAsString()}: {Context.Item.Record.RepoRelativePath}");
                    Console.WriteLine("For more information about the current location:");
                    Console.WriteLine(
                        "    ",
                        "mrepo info".ThemedHighlight(Console.Theme));
                    Console.WriteLine("If you want to know more about a specific item:");
                    Console.WriteLine(
                        "    ",
                        "mrepo explain".ThemedHighlight(Console.Theme),
                        " <path-to-file-or-directory>".ThemedLowlight(Console.Theme));
                    break;
            }

            return Task.FromResult(ExitCodes.SUCCESS);
        }

        private void InitialiseExplanations()
        {
            _explanationMapping[Constants.SOURCE_DIRECTORY_NAME] = () =>
            {
                Console.WriteLine("A folder which is reserved for source code.");
            };

            _explanationMapping["docs"] = () =>
            {
                Console.WriteLine("A folder which is reserved for technical documentation.");
            };

            _explanationMapping[Constants.TEST_DIRECTORY_NAME] = () =>
            {
                Console.WriteLine("A folder which is reserved for unit tests.");
            };

            _explanationMapping[FileNames.DotGitIgnore] = () =>
            {
                Console.WriteLine("The file specifies intentionally untracked files that Git should ignore.");
                Console.WriteLine("    https://git-scm.com/docs/gitignore");
            };

            _explanationMapping[FileNames.DotGitAttributes] = () =>
            {
                Console.WriteLine("The .gitattributes file allows you to specify the files and paths attributes that should be used by git when performing git actions, such as git commit.");
                Console.WriteLine("    https://www.git-scm.com/docs/gitattributes");
            };

            _explanationMapping[FileNames.DotEditorConfig] = () =>
            {
                Console.WriteLine("EditorConfig helps maintain consistent coding styles for multiple developers working on the same project across various editors and IDEs.");
                Console.WriteLine("    https://editorconfig.org/");
            };

            _explanationMapping[FileNames.GlogalJson] = () =>
            {
                Console.WriteLine("The global.json file allows you to define which .NET SDK version is used.");
                Console.WriteLine("    https://docs.microsoft.com/en-us/dotnet/core/tools/global-json?tabs=netcore3x");
            };

            _explanationMapping[FileNames.DotVsConfig] = () =>
            {
                Console.WriteLine("With the .vsconfig file you can configure Visual Studio across your organization.");
                Console.WriteLine("    https://docs.microsoft.com/en-us/visualstudio/install/import-export-installation-configurations?view=vs-2022");
            };

            _explanationMapping[FileNames.MrepoJson] = () =>
            {
                Console.WriteLine("The configuration of the mono-repo, can define custom behaviors, scripts or properties of projects/worksteads.");
                Console.WriteLine("    https://github.com/akobr/mono.me/blob/main/docs/Monorepo/mrepo-json.md");
            };

            _explanationMapping[FileNames.VersionJson] = () =>
            {
                Console.WriteLine("Defines version for a specific subtree in the mono-repo.");
                Console.WriteLine("    https://github.com/akobr/mono.me/blob/main/docs/Monorepo/versioning.md");
            };

            _explanationMapping[FileNames.DirectoryBuildProps] = () =>
            {
                Console.WriteLine("Directory.Build.props is a user-defined file that provides customizations to projects under a directory structure.");
                Console.WriteLine("    https://docs.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022#directorybuildprops-and-directorybuildtargets");
            };

            _explanationMapping[FileNames.DirectoryBuildTargets] = () =>
            {
                Console.WriteLine("Directory.Build.targets is a user-defined file that provides customizations to projects build under a directory structure.");
                Console.WriteLine("    https://docs.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022#directorybuildprops-and-directorybuildtargets");
            };

            _explanationMapping[FileNames.DirectoryBuildProj] = () =>
            {
                Console.WriteLine("Allows project tree owners the ability to define what projects should be built.");
                Console.WriteLine("    https://github.com/microsoft/MSBuildSdks/blob/main/src/Traversal/README.md");
            };

            _explanationMapping[FileNames.NugetConfig] = () =>
            {
                Console.WriteLine("A nuget.config file allow you to store settings in different locations so that they apply to a single project, a group of projects, or all projects.");
                Console.WriteLine("    https://docs.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior");
            };

            _explanationMapping[FileNames.StylecopJson] = () =>
            {
                Console.WriteLine("A configuration for StyleCop, you can fine-tune the behavior of certain rules.");
                Console.WriteLine("    https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/Configuration.md");
            };
        }
    }
}
