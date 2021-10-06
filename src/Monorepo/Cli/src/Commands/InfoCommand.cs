using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command("info", Description = "Display info of a location in the mono-repository.")]
    public class InfoCommand : BaseCommand
    {
        private readonly IItemOptionsProvider optionsProvider;

        public InfoCommand(
            IExtendedConsole console,
            ICommandContext context,
            IItemOptionsProvider optionsProvider)
            : base(console, context)
        {
            this.optionsProvider = optionsProvider;
        }

        protected override async Task ExecuteAsync()
        {
            var item = Context.Item;
            var record = item.Record;
            var exactVersions = await item.GetExactVersionsAsync();
            var options = optionsProvider.TryGetOptions(record.Path);

            Console.WriteHeader(
                $"{record.GetTypeAsString()}: ",
                record.Name.ThemedHighlight(Console.Theme),
                $" {exactVersions.Version}");

            Console.WriteLine(
                $"Path: {record.Path.GetRelativePath(Context.Repository.Record.Path)}".ThemedLowlight(Console.Theme));
            Console.WriteLine(
                $"Version: {exactVersions.PackageVersion}".ThemedLowlight(Console.Theme));

            if (options.Exclude.Contains(Excludes.Version))
            {
                Console.WriteLine("The versioning is disabled.".ThemedLowlight(Console.Theme));
                Console.WriteLine("Not releasable by the tool.");
            }
            else if (options.Exclude.Contains(Excludes.Release))
            {
                Console.WriteLine("Not releasable by the tool.");
            }

            var files = await GetFilesAsync(item, Context.Repository);

            if (!options.Exclude.Contains(Excludes.Version))
            {
                Console.WriteLine();
                Console.WriteHeader("Version definition");
                var root = new Composition(string.Empty);
                files.versionFiles.Aggregate(root, (p, s) =>
                {
                    var node = new Composition(s);
                    p.Children.Add(node);
                    return node;
                });
                Console.WriteTree(root.Children.First(), n => n);
            }

            Console.WriteLine();
            Console.WriteHeader("Package reference definitions");
            var rootPackages = new Composition(string.Empty);
            files.packageFiles.Aggregate(rootPackages, (p, s) =>
            {
                var node = new Composition(s);
                p.Children.Add(node);
                return node;
            });
            Console.WriteTree(rootPackages.Children.First(), n => n);

            if (item is IProject project)
            {
                var internalDependencies = await project.GetInternalDependenciesAsync();

                if (internalDependencies.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteHeader("Project references:");
                    Console.WriteTable(
                        internalDependencies,
                        d => new[] { $"> {d.Name}", d.Path.GetRelativePath(Context.Repository.Record.Path) },
                        new[] { "Project", "Repository path" });
                }
            }

            var externalDependencies = await item.GetExternalDependenciesAsync();

            if (externalDependencies.Count > 0)
            {
                Console.WriteLine();
                Console.WriteHeader("Package references:");
                Console.WriteTable(
                    externalDependencies,
                    d => new[] { $"> {d.Name}", d.Version.ToString() },
                    new[] { "Package", "Version" });
            }
        }

        private static async Task<(IEnumerable<string> versionFiles, IEnumerable<string> packageFiles)> GetFilesAsync(IItem item, IRepository repository)
        {
            var workItem = item;
            var versionFiles = new List<string>();
            var packageFiles = new List<string>();
            var lastVersionFile = string.Empty;
            var lastPackagesFile = string.Empty;

            while (workItem != null)
            {
                var versionFile = await workItem.TryGetVersionFilePathAsync();
                var packagesFile = await workItem.TryGetPackagesFilePathAsync();

                if (!string.IsNullOrEmpty(versionFile)
                    && versionFile != lastVersionFile)
                {
                    versionFiles.Add(versionFile.GetRelativePath(repository.Record.Path));
                    lastVersionFile = versionFile;
                }

                if (!string.IsNullOrEmpty(packagesFile)
                    && packagesFile != lastPackagesFile)
                {
                    packageFiles.Add(packagesFile.GetRelativePath(repository.Record.Path));
                    lastPackagesFile = packagesFile;
                }

                workItem = workItem.Parent;
            }

            return (versionFiles, packageFiles);
        }
    }
}
