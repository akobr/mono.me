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
    [Command(CommandNames.USAGES, Description = "Show usages of a current location.")]
    public class ShowUsagesCommand : BaseCommand
    {
        public ShowUsagesCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Option("-s|--search", CommandOptionType.SingleValue, Description = "Will try to search any directory/repository for usages.")]
        public string? SearchPath { get; }

        [Option("-p|--as-package", CommandOptionType.NoValue, Description = "Determining whether to include search for usages as package.")]
        public bool SearchAsPackage { get; }

        protected override async Task<int> ExecuteAsync()
        {
            if (Context.Item is not IProject targetProject)
            {
                Console.WriteImportant("This command can be called only on project.");
                return ExitCodes.ERROR_WRONG_PLACE;
            }

            if (!string.IsNullOrWhiteSpace(SearchPath))
            {
                await ExternalSearchAsync(targetProject);
                return ExitCodes.SUCCESS;
            }

            var record = targetProject.Record;
            var packageName = record.Name;
            var internalMap = new HashSet<IIdentifier>();
            var externalMap = new HashSet<IIdentifier>();
            var searchForPackageUsage = SearchAsPackage && await targetProject.GetIsPackableAsync();

            if (searchForPackageUsage)
            {
                packageName = await targetProject.GetPackageNameAsync();
            }

            foreach (var project in Context.Repository.GetAllProjects())
            {
                var internalDependencies = await project.GetInternalDependenciesAsync();

                if (internalDependencies.Any(d => MonorepoDirectoryFunctions.GetRecord(d.FullPath).Equals(record)))
                {
                    internalMap.Add(project.Record.Identifier);
                }

                if (searchForPackageUsage)
                {
                    var externalDependencies = await project.GetExternalDependenciesAsync();

                    if (externalDependencies.Any(d => d.Name.EqualsOrdinalIgnoreCase(packageName)))
                    {
                        externalMap.Add(project.Record.Identifier);
                    }
                }
            }

            var noUsage = internalMap.Count < 1 && externalMap.Count < 1;

            Console.WriteHeader("Usage of ", record.Identifier.Humanized.ThemedHighlight(Console.Theme));

            if (internalMap.Count < 1)
            {
                Console.WriteLine("There is NO internal usage of the project.");
            }
            else
            {
                var tree = BuildTree(Context.Repository, internalMap);
                Console.WriteTree(tree, n => n);
            }

            if (searchForPackageUsage)
            {
                Console.WriteLine();
                Console.WriteHeader("Usage of ", packageName.ThemedHighlight(Console.Theme), " package");

                if (externalMap.Count < 1)
                {
                    Console.WriteLine("There is NO usage of the package.");
                }
                else
                {
                    var tree = BuildTree(Context.Repository, externalMap);
                    Console.WriteTree(tree, n => n);
                }
            }

            if (noUsage)
            {
                Console.WriteLine();
                Console.WriteLine("Do you know that you can search for usages beyond the mono-repository?".ThemedLowlight(Console.Theme));
                Console.WriteLine("Just run same command with option ".ThemedLowlight(Console.Theme), "--search <path-to-search>");
            }

            return ExitCodes.SUCCESS;
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

            var packageName = await project.GetPackageNameAsync();

            Console.WriteHeader(".NET project files");
            await SearchUsagesAsync(
                path,
                "*.*proj",
                (content, filePath) => SearchProjectFile(content, filePath, packageName));

            Console.WriteHeader("MsBuild properties files");
            await SearchUsagesAsync(
                path,
                "*.props",
                (content, filePath) => SearchPackagesProps(content, filePath, packageName));

            Console.WriteHeader("NuGet packages config files");
            await SearchUsagesAsync(
                path,
                "packages.config",
                (content, filePath) => SearchPackagesConfig(content, filePath, packageName));
        }

        private static async Task<ExternalUsage?> SearchProjectFile(Stream content, string filePath, string targetPackageName)
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

                var version = xReference.Attribute("Version")?.Value // Default versioning inside project file
                              ?? xReference.Attribute("UpdatedVersion")?.Value; // Microsoft.Build.CentralPackageVersions
                return new ExternalUsage(filePath, version);
            }

            return null;
        }

        private static async Task<ExternalUsage?> SearchPackagesConfig(Stream content, string filePath, string targetPackageName)
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

        private static async Task<ExternalUsage?> SearchPackagesProps(Stream content, string filePath, string targetPackageName)
        {
            var document = await XDocument.LoadAsync(content, LoadOptions.None, default);
            var root = document.Root;

            if (root is null)
            {
                return null;
            }

            // Microsoft.Build.CentralPackageVersions
            var allReferences = document.Descendants(root.GetDefaultNamespace() + "PackageReference");

            // .NET core SDK 3.1.300 Directory.Packages.props
            allReferences = allReferences.Concat(document.Descendants(root.GetDefaultNamespace() + "PackageVersion"));

            foreach (var xReference in allReferences)
            {
                var packageName = xReference.Attribute("Include")?.Value // Standard package reference
                                  ?? xReference.Attribute("Update")?.Value; // Microsoft.Build.CentralPackageVersions

                if (string.IsNullOrEmpty(packageName)
                    || !packageName.EqualsOrdinalIgnoreCase(targetPackageName))
                {
                    continue;
                }

                var version = xReference.Attribute("Version")?.Value // Default versioning
                              ?? xReference.Attribute("VersionOverride")?.Value; // Microsoft.Build.CentralPackageVersions

                return new ExternalUsage(filePath, version);
            }

            return null;
        }

        private async Task SearchUsagesAsync(string directoryPath, string fileFilter, Func<Stream, string, Task<ExternalUsage?>> processingFunction)
        {
            try
            {
                var hasResult = false;

                await foreach (var usage in GetExternalUsagesAsync(directoryPath, fileFilter, processingFunction))
                {
                    Console.WriteLine(
                        usage.FilePath.GetRelativePath(directoryPath),
                        usage.Version is not null
                            ? $" [{usage.Version}]".ThemedLowlight(Console.Theme)
                            : new Span());
                    hasResult = true;
                }

                if (!hasResult)
                {
                    Console.WriteLine("Nothing has been found.".ThemedLowlight(Console.Theme));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error during search of '{directoryPath}/{fileFilter}': ".ThemedLowlight(Console.Theme), e.Message);
            }

            Console.WriteLine();
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
