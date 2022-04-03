using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace _42.Monorepo.Cli.NuGet
{
    public class PackagesDefinitionManager
    {
        private readonly AsyncLazy<List<string>> _fileContent;
        private readonly string _packagesFilePath;

        public PackagesDefinitionManager(string packagesFilePath)
        {
            _packagesFilePath = packagesFilePath;
            _fileContent = new AsyncLazy<List<string>>(LoadPackagesDefinitionFileAsync);
        }

        public string DefinitionFilePath => _packagesFilePath;

        public async Task<bool> IsPackageDefinedInAsync(string packageId)
        {
            var lines = await _fileContent.GetValueAsync();
            var isDefined = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (line.Contains(packageId))
                {
                    isDefined = true;
                    break;
                }
            }

            return isDefined;
        }

        public async Task<string> GetPackageVersionAsync(string packageId)
        {
            var lines = await _fileContent.GetValueAsync();

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (line.Contains(packageId))
                {
                    var indexOfStartVersion = line.IndexOf("Version=\"") + 9;
                    var indexOfEndVersion = line.IndexOf('\"', indexOfStartVersion);
                    return line[indexOfStartVersion..indexOfEndVersion];
                }
            }

            return string.Empty;
        }

        public async Task AddOrUpdatePackageAsync(string packageId, string packageVersion)
        {
            if (await TryUpdatePackageVersionAsync(packageId, packageVersion))
            {
                return;
            }

            var lines = await _fileContent.GetValueAsync();
            var orderOfPackages = new SortedList<string, int>();

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var lineReadIndex = 0;
                var packageDefinitionStartIndex = -1;

                while ((packageDefinitionStartIndex = line.IndexOf("<PackageVersion Include=\"", lineReadIndex)) != -1)
                {
                    packageDefinitionStartIndex += 25;
                    var indexOfEndPackageId = line.IndexOf('\"', packageDefinitionStartIndex);
                    orderOfPackages[line[packageDefinitionStartIndex..indexOfEndPackageId]] = i;
                    lineReadIndex = indexOfEndPackageId;
                }

                if (line.Contains("<ItemGroup>"))
                {
                    orderOfPackages[string.Empty] = i;
                }
            }

            orderOfPackages[packageId] = -1;
            var indexOfPackage = orderOfPackages.IndexOfKey(packageId);
            var targetLineIndex = orderOfPackages.Values[indexOfPackage - 1];
            lines.Insert(targetLineIndex + 1, $"    <PackageVersion Include=\"{packageId}\" Version=\"{packageVersion}\" />");
        }

        public async Task<bool> TryUpdatePackageVersionAsync(string packageId, string packageVersion)
        {
            var lines = await _fileContent.GetValueAsync();
            var updated = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (line.Contains(packageId))
                {
                    var indexOfStartVersion = line.IndexOf("Version=\"");
                    var indexOfEndVersion = line.IndexOf('\"', indexOfStartVersion + 9) + 1;
                    lines[i] = line[..indexOfStartVersion] + $"Version=\"{packageVersion}\"" + line[indexOfEndVersion..];
                    updated = true;
                    break;
                }
            }

            return updated;
        }

        public async Task<bool> RemovePackageAsync(string packageId)
        {
            var lines = await _fileContent.GetValueAsync();
            var isRemoved = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (line.Contains(packageId))
                {
                    lines.RemoveAt(i);
                    isRemoved = true;
                    break;
                }
            }

            return isRemoved;
        }

        public async Task SaveAsync()
        {
            await File.WriteAllLinesAsync(_packagesFilePath, await _fileContent.GetValueAsync());
        }

        private async Task<List<string>> LoadPackagesDefinitionFileAsync()
        {
            return (await File.ReadAllLinesAsync(_packagesFilePath)).ToList();
        }
    }
}
