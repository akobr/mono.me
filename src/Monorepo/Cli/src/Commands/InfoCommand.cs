using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Items;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.INFO, Description = "Display information of a current location.")]
    public class InfoCommand : BaseCommand
    {
        private readonly IItemOptionsProvider _optionsProvider;

        public InfoCommand(
            IExtendedConsole console,
            ICommandContext context,
            IItemOptionsProvider optionsProvider)
            : base(console, context)
        {
            _optionsProvider = optionsProvider;
        }

        protected override async Task<int> ExecuteAsync()
        {
            var item = Context.Item;
            var record = item.Record;
            var exactVersions = await item.GetExactVersionsAsync();
            var options = _optionsProvider.GetOptions(record.RepoRelativePath);

            WriteItemInfo(options);

            Console.WriteHeader(
                $"{record.GetTypeAsString()}: ",
                record.Name.ThemedHighlight(Console.Theme),
                $" {exactVersions.Version}");

            Console.WriteLine(
                $"Path: {record.Path.GetRelativePath(Context.Repository.Record.Path)}".ThemedLowlight(Console.Theme));
            Console.WriteLine(
                $"Version: {exactVersions.PackageVersion}".ThemedLowlight(Console.Theme));

            if (options.Exclude.Contains(Excludes.VERSION))
            {
                Console.WriteLine("The versioning is disabled (non-releasable).".ThemedLowlight(Console.Theme));
            }
            else if (options.Exclude.Contains(Excludes.RELEASE))
            {
                Console.WriteLine("Explicitly excluded from a release.");
            }

            var files = await GetFilesAsync(item, Context.Repository);

            if (files.VersionFiles.Count > 0 && !options.Exclude.Contains(Excludes.VERSION))
            {
                Console.WriteLine();
                Console.WriteHeader("Definition of version");
                var root = new Composition(string.Empty);
                files.VersionFiles.Aggregate(root, (p, s) =>
                {
                    var node = new Composition(s);
                    p.Children.Add(node);
                    return node;
                });
                Console.WriteTree(root.Children.First(), n => n);
            }

            if (files.PackageFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteHeader("Definition of package dependencies");
                var rootPackages = new Composition(string.Empty);
                files.PackageFiles.Aggregate(rootPackages, (p, s) =>
                {
                    var node = new Composition(s);
                    p.Children.Add(node);
                    return node;
                });
                Console.WriteTree(rootPackages.Children.First(), n => n);
            }

            // Show dependencies only for projects (not for repositories and worksteads)
            if (item is IProject project)
            {
                var internalDependencies = await project.GetInternalDependenciesAsync();

                if (internalDependencies.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteHeader("Project references");
                    Console.WriteTable(
                        internalDependencies.OrderBy(d => d.Name),
                        d => new[] { $"> {d.Name}", d.RepoRelativePath },
                        new[] { "Project", "Repository path" });
                }

                var externalDependencies =
                    (await item.GetExternalDependenciesAsync())
                    .Where(d => d.IsDirect);

                if (externalDependencies.Any())
                {
                    Console.WriteLine();
                    Console.WriteHeader("Package dependencies");
                    Console.WriteTable(
                        externalDependencies.OrderBy(d => d.Name),
                        d => new[] { $"> {d.Name}", d.Version.ToString() },
                        new[] { "Package", "Version" });
                }
            }

            return ExitCodes.SUCCESS;
        }

        private void WriteItemInfo(ItemOptions options)
        {
            var someInfo = false;

            if (!string.IsNullOrWhiteSpace(options.Name))
            {
                Console.WriteHeader(options.Name);
                someInfo = true;
            }

            if (!string.IsNullOrWhiteSpace(options.Description))
            {
                Console.WriteLine(options.Description.ThemedLowlight(Console.Theme));
                someInfo = true;
            }

            if (someInfo)
            {
                Console.WriteLine();
            }
        }

        private static async Task<(IReadOnlyCollection<string> VersionFiles, IReadOnlyCollection<string> PackageFiles)> GetFilesAsync(IItem item, IRepository repository)
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
