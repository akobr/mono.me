using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Cli.NuGet;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;
using Semver;
using Sharprompt;

namespace _42.Monorepo.Cli.Commands.New
{
    [Command(CommandNames.PACKAGE, Description = "Add new package to centralized definition.")]
    public class NewPackageCommand : BaseCommand
    {
        public NewPackageCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, Description = "Id of the package.")]
        public string? PackageId { get; set; } = string.Empty;

        [Argument(1, Description = "Target version of the package.")]
        public string? PackageVersion { get; set; } = string.Empty;

        [Option("-p|--prerelease", CommandOptionType.NoValue, Description = "Determining whether to include pre-release versions.")]
        public bool UsePrereleases { get; set; }

        protected override async Task<int> ExecuteAsync()
        {
            var packageId = PackageId;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                Console.WriteImportant("The package id needs to be specified.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

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

            foreach (var packagesManager in packagesFilePaths.Select(p => new PackagesDefinitionManager(p)))
            {
                if (await packagesManager.TryUpdatePackageVersionAsync(packageId, targetVersion))
                {
                    await packagesManager.SaveAsync();
                    return ExitCodes.SUCCESS;
                }
            }

            var directManager = new PackagesDefinitionManager(packagesFilePaths.First());
            await directManager.AddOrUpdatePackageAsync(packageId, targetVersion);
            await directManager.SaveAsync();

            return ExitCodes.SUCCESS;
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
