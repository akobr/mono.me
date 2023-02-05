using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.NuGet;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;
using Semver;
using Sharprompt;

namespace _42.Monorepo.Cli.Commands.Update
{
    [Command(CommandNames.PACKAGE, Description = "Update version of a specific package.")]
    public class UpdatePackageCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;

        public UpdatePackageCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context)
            : base(console, context)
        {
            _fileSystem = fileSystem;
            // no operation
        }

        [Argument(0, Description = "Id of the package.")]
        public string? PackageId { get; set; } = string.Empty;

        [Argument(1, Description = "Target version of the package.")]
        public string? PackageVersion { get; set; } = string.Empty;

        [Option("-p|--prerelease", CommandOptionType.NoValue, Description = "Determining whether to include pre-release versions.")]
        public bool UsePrereleases { get; set; }

        [Option("-m|--move", CommandOptionType.NoValue, Description = "Determining whether you want to possibly move the package version definition.")]
        public bool WantMovePackage { get; set; }

        protected override async Task<int> ExecuteAsync()
        {
            var packageId = await GetPackageId();
            var targetVersion = await GetTargetVersionAsync(packageId);

            if (string.IsNullOrEmpty(targetVersion))
            {
                Console.WriteImportant(
                    "No version of the package '",
                    packageId.ThemedHighlight(Console.Theme),
                    "' found on nuget.org feed.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            var packagesFilePaths = await GetPossiblePackagesFilePathsAsync();

            if (packagesFilePaths.Count < 1)
            {
                Console.WriteImportant("There is no centralized package definition.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            if (!WantMovePackage || packagesFilePaths.Count < 2)
            {
                foreach (var packagesManager in packagesFilePaths.Select(p => new PackagesDefinitionManager(p, _fileSystem)))
                {
                    if (await packagesManager.TryUpdatePackageVersionAsync(packageId, targetVersion))
                    {
                        await packagesManager.SaveAsync();
                        return ExitCodes.SUCCESS;
                    }
                }

                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var currentPackagesFile = string.Empty;

            foreach (var packagesManager in packagesFilePaths.Select(p => new PackagesDefinitionManager(p, _fileSystem)))
            {
                if (await packagesManager.IsPackageDefinedInAsync(packageId))
                {
                    currentPackagesFile = packagesManager.DefinitionFilePath;
                    break;
                }
            }

            var currentPackagesFileRelative = currentPackagesFile.GetRelativePath(Context.Repository.Record.Path);
            Console.WriteLine($"Currently the package is defined in: {currentPackagesFileRelative}");
            var pickedPackagesFile = Console.Select(new SelectOptions<string>
            {
                Items = packagesFilePaths.Select(p => p.GetRelativePath(Context.Repository.Record.Path)),
                DefaultValue = currentPackagesFileRelative,
                Message = "Into which Directory.Packages.props file you want to move",
            });

            if (currentPackagesFile != pickedPackagesFile)
            {
                var currentManager = new PackagesDefinitionManager(currentPackagesFile, _fileSystem);
                await currentManager.RemovePackageAsync(packageId);
                await currentManager.SaveAsync();
            }

            var pickedManager = new PackagesDefinitionManager(pickedPackagesFile, _fileSystem);
            await pickedManager.AddOrUpdatePackageAsync(packageId, targetVersion);
            await pickedManager.SaveAsync();

            return ExitCodes.SUCCESS;
        }

        private async Task<string> GetPackageId()
        {
            var packageId = PackageId;
            var externalDependencies = await Context.Item.GetExternalDependenciesAsync();
            var possiblePackageNames = new SortedSet<string>(externalDependencies.Select(d => d.Name));

            if (string.IsNullOrWhiteSpace(packageId)
                || !possiblePackageNames.Contains(packageId))
            {
                packageId = Console.Select(new SelectOptions<string>()
                {
                    Items = possiblePackageNames,
                    Message = "Select which package to update",
                    PageSize = 20,
                });
            }

            return packageId;
        }

        private async Task<string> GetTargetVersionAsync(string packageId)
        {
            var packageVersion = PackageVersion;
            var nugetManager = new NuGetFeedManager(UsePrereleases);
            var possibleVersions = await nugetManager.GetPossibleVersionsAsync(packageId);

            if (string.IsNullOrWhiteSpace(packageVersion)
                || !SemVersion.TryParse(packageVersion, out var semVersion))
            {
                if (possibleVersions.Count < 1)
                {
                    return string.Empty;
                }

                semVersion = Console.Select(new SelectOptions<SemVersion>()
                {
                    Items = possibleVersions,
                    Message = "Select a target version",
                    TextSelector = v => v.ToString(),
                    PageSize = 20,
                });
            }

            return semVersion.ToString();
        }

        private async Task<IReadOnlyCollection<string>> GetPossiblePackagesFilePathsAsync()
        {
            var packageFiles = new List<string>();
            var lastPackagesFile = string.Empty;
            var workItem = Context.Item;

            while (workItem != null)
            {
                var packagesFile = await workItem.TryGetPackagesFilePathAsync();

                if (!string.IsNullOrEmpty(packagesFile)
                    && packagesFile != lastPackagesFile)
                {
                    packageFiles.Add(packagesFile);
                    lastPackagesFile = packagesFile;
                }

                workItem = workItem.Parent;
            }

            return packageFiles;
        }
    }
}
