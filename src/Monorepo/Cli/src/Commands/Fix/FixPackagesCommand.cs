using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.NuGet;
using _42.Monorepo.Cli.Operations.Strategies;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Fix
{
    [Command(CommandNames.PACKAGES, Description = "Move all locally versioned packages to centralized point.")]
    public class FixPackagesCommand : BaseCommand
    {
        private readonly IFileSystem _fileSystem;

        public FixPackagesCommand(
            IFileSystem fileSystem,
            IExtendedConsole console,
            ICommandContext context)
            : base(console, context)
        {
            _fileSystem = fileSystem;
            // no operation
        }

        protected override async Task<int> ExecuteAsync()
        {
            var packagesFilePaths = await GetPossiblePackagesFilePathsAsync();

            if (packagesFilePaths.Count < 1)
            {
                Console.WriteImportant("There is no centralized package definition.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            var projects = Context.Item.Record.Type == Model.RecordType.Project
                ? new IProject[] { (IProject)Context.Item }
                : ((ICompositionOfProjects)Context.Item).GetAllProjects();
            var modifiedFiles = new List<string>();

            foreach (var project in projects)
            {
                var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(project, _fileSystem);
                if (await TryFixDecentralizedPackageVersionsAsync(projectFilePath, packagesFilePaths))
                {
                    modifiedFiles.Add(projectFilePath.GetRelativePath(Context.Repository.Record.Path));
                }
            }

            if (modifiedFiles.Count < 1)
            {
                Console.WriteLine("Nothing to repair.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            Console.WriteHeader("Modified project file(s):");

            foreach (var file in modifiedFiles)
            {
                Console.WriteLine($" > {file}");
            }

            return ExitCodes.SUCCESS;
        }

        private async Task<bool> TryFixDecentralizedPackageVersionsAsync(string projectFilePath, IReadOnlyCollection<string> packagesFilePaths)
        {
            var lines = await _fileSystem.File.ReadAllLinesAsync(projectFilePath);
            var packageIdRegex = new Regex("Include=\"(?<packageId>[a-zA-Z0-9\\-\\.]+)\"", RegexOptions.Compiled);
            var versionRegex = new Regex("Version=\"(?<packageVersion>[a-zA-Z0-9\\-\\.\\+]*)\"", RegexOptions.Compiled);
            var packagesToUpdate = new Dictionary<string, string>();
            var hasChange = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("<PackageReference")
                    && versionRegex.IsMatch(line))
                {
                    var idMatch = packageIdRegex.Match(line);

                    if (idMatch.Success)
                    {
                        var packageId = idMatch.Groups["packageId"].Value;
                        var packageVersion = versionRegex.Match(line).Groups["packageVersion"].Value;
                        packagesToUpdate[packageId] = packageVersion;
                    }

                    lines[i] = versionRegex.Replace(line, string.Empty);
                    hasChange = true;
                }
            }

            if (!hasChange)
            {
                return false;
            }

            await _fileSystem.File.WriteAllLinesAsync(projectFilePath, lines);

            foreach (var packagesManager in packagesFilePaths.Select(p => new PackagesDefinitionManager(p, _fileSystem)))
            {
                foreach (var packagePair in packagesToUpdate.ToList())
                {
                    if (await packagesManager.TryUpdatePackageVersionAsync(packagePair.Key, packagePair.Value))
                    {
                        await packagesManager.SaveAsync();
                        packagesToUpdate.Remove(packagePair.Key);
                    }
                }
            }

            if (packagesToUpdate.Count > 0)
            {
                var directManager = new PackagesDefinitionManager(packagesFilePaths.First(), _fileSystem);

                foreach (var packagePair in packagesToUpdate)
                {
                    await directManager.AddOrUpdatePackageAsync(packagePair.Key, packagePair.Value);
                }

                await directManager.SaveAsync();
            }

            return true;
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
