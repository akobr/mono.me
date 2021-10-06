using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Output;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Show
{
    [Command("usages", Description = "Show usages of current project/workstead.")]
    public class ShowUsagesCommand : BaseCommand
    {
        public ShowUsagesCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Option("-s|--search", CommandOptionType.SingleValue, Description = "Will try to search any directory/repository for usages.")]
        public string? SearchPath { get; }

        protected override async Task ExecuteAsync()
        {
            if (Context.Item is not IProject targetProject)
            {
                Console.WriteImportant("This command can be called only on project.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(SearchPath))
            {
                await ExternalSearchAsync(targetProject);
                return;
            }

            var record = targetProject.Record;
            var projectMap = new HashSet<IIdentifier>();

            foreach (var project in Context.Repository.GetAllProjects())
            {
                var dependencies = await project.GetInternalDependenciesAsync();
                if (dependencies.Any(d => record == MonorepoDirectoryFunctions.GetRecord(d.Path)))
                {
                    projectMap.Add(project.Record.Identifier);
                }
            }

            Console.WriteHeader("Usage of ", record.Identifier.Humanized.ThemedHighlight(Console.Theme));

            if (projectMap.Count < 1)
            {
                Console.WriteLine("There is NO internal usage of the project.");

                Console.WriteLine();
                Console.WriteLine("Do you know that you can search for usages beyond the mono-repository?".ThemedLowlight(Console.Theme));
                Console.WriteLine("Just run same command with option ".ThemedLowlight(Console.Theme), "--search <path-to-search>");
            }
            else
            {
                var tree = BuildTree(Context.Repository, projectMap);
                Console.WriteTree(tree, n => n);
            }
        }

        private static Composition BuildTree(IRepository repository, IReadOnlySet<IIdentifier> projectMap)
        {
            var root = new Composition(repository.Record.Name.Lowlight());

            foreach (var workstead in repository.GetWorksteads())
            {
                FillTree(workstead, root, projectMap);
            }

            return root;
        }

        private static bool FillTree(IWorkstead workstead, Composition parentNode, IReadOnlySet<IIdentifier> projectMap)
        {
            var node = new Composition(workstead.Record.Name);
            var shouldBeVisible = false;

            foreach (var subWorkstead in workstead.GetSubWorksteads())
            {
                if (FillTree(subWorkstead, node, projectMap))
                {
                    shouldBeVisible = true;
                }
            }

            foreach (var project in workstead.GetProjects())
            {
                if (projectMap.Contains(project.Record.Identifier))
                {
                    shouldBeVisible = true;
                    node.Children.Add(project.Record.Name.Highlight());
                }
            }

            if (shouldBeVisible)
            {
                parentNode.Children.Add(node);
            }

            return shouldBeVisible;
        }

        private async Task ExternalSearchAsync(IProject project)
        {
            var path = SearchPath ?? MonorepoDirectoryFunctions.GetMonorepoRootDirectory();

            if (!Directory.Exists(SearchPath))
            {
                Console.WriteImportant("The specified directory doesn't exist.");
                return;
            }

            if (!await project.GetIsPackableAsync())
            {
                Console.WriteImportant("The project is not served as package.");
                return;
            }

            var packageName = project.GetPackageNameAsync();

            // Project files
            foreach (var filePath in Directory.GetFiles(path, "*.*proj", SearchOption.AllDirectories))
            {
                
            }

            // props files
            foreach (string filePath in Directory.GetFiles(path, "*.props", SearchOption.AllDirectories))
            {

            }

            // packages.json
            foreach (string filePath in Directory.GetFiles(path, "packages.config", SearchOption.AllDirectories))
            {

            }
        }

        private async Task<ExternalUsage?> SearchProject(Stream content, string filePath, string targetPackageName)
        {
            var document = await XDocument.LoadAsync(content, LoadOptions.None, default);
            var root = document.Root;

            if (root is null)
            {
                return null;
            }

            foreach (var xReference in document.Descendants(root.GetDefaultNamespace() + "PackageReference"))
            {
                var packageName = xReference.Attribute("Include")?.Value;

                if (string.IsNullOrEmpty(packageName)
                    || !packageName.EqualsOrdinalIgnoreCase(targetPackageName))
                {
                    continue;
                }

                var version = xReference.Attribute("Version")?.Value ?? xReference.Attribute("UpdatedVersion")?.Value;
                return new ExternalUsage(filePath, version);
            }

            return null;
        }

        private async Task<ExternalUsage?> SearchPackagesConfig(Stream content, string filePath, string targetPackageName)
        {
            var document = await XDocument.LoadAsync(content, LoadOptions.None, default);
            var root = document.Root;

            if (root is null)
            {
                return null;
            }

            foreach (var xReference in document.Descendants(root.GetDefaultNamespace() + "package"))
            {
                var packageName = xReference.Attribute("id")?.Value;

                if (string.IsNullOrEmpty(packageName)
                    || !packageName.EqualsOrdinalIgnoreCase(targetPackageName))
                {
                    continue;
                }

                var version = xReference.Attribute("version")?.Value;
                return new ExternalUsage(filePath, version);
            }

            return null;
        }

        private async Task<ExternalUsage?> SearchPackagesProps(Stream content, string filePath, string targetPackageName)
        {
            var document = await XDocument.LoadAsync(content, LoadOptions.None, default);
            var root = document.Root;

            if (root is null)
            {
                return null;
            }

            foreach (var xReference in document.Descendants(root.GetDefaultNamespace() + "PackageReference"))
            {
                var packageName = xReference.Attribute("Update")?.Value ?? xReference.Attribute("Include")?.Value;

                if (string.IsNullOrEmpty(packageName)
                    || !packageName.EqualsOrdinalIgnoreCase(targetPackageName))
                {
                    continue;
                }

                var version = xReference.Attribute("Version")?.Value;
                return new ExternalUsage(filePath, version);
            }

            return null;
        }

        private async Task SearchUsagesAsync(string directoryPath, string fileFilter, Func<Stream, string, Task<ExternalUsage?>> processingFunction)
        {
            try
            {
                await foreach (var usage in GetExternalUsagesAsync(directoryPath, fileFilter, processingFunction))
                {
                    Console.WriteLine(
                        usage.FilePath.GetRelativePath(directoryPath),
                        usage.Version is not null
                            ? $" [{usage.Version}]".ThemedLowlight(Console.Theme)
                            : new Span());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during search of '{directoryPath}/{fileFilter}': ".ThemedLowlight(Console.Theme), e.Message);
            }
        }

        private async IAsyncEnumerable<ExternalUsage> GetExternalUsagesAsync(string directoryPath, string fileFilter, Func<Stream, string, Task<ExternalUsage?>> processingFunction)
        {
            foreach (var filePath in Directory.GetFiles(directoryPath, fileFilter, SearchOption.AllDirectories))
            {
                var usage = await SearchFileAsync(filePath, processingFunction);

                if (usage != null)
                {
                    yield return usage;
                }
            }
        }

        private async Task<ExternalUsage?> SearchFileAsync(string filePath, Func<Stream, string, Task<ExternalUsage?>> processingFunction)
        {
            try
            {
                await using var reader = File.OpenRead(filePath);
                return await processingFunction(reader, filePath);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during search of '{filePath}': ".ThemedLowlight(Console.Theme), e.Message);
                return null;
            }
        }

        private class ExternalUsage
        {
            public ExternalUsage(string filePath, string? version = null)
            {
                FilePath = filePath;
                Version = version;
            }

            public string FilePath { get; }

            public string? Version { get; }
        }

    }
}
