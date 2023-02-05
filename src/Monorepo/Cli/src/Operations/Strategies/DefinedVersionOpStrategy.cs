using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Versioning;
using Microsoft.Extensions.Logging;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class DefinedVersionOpStrategy : IOpStrategy<IVersionTemplate?>
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFileContentCache _fileCache;
        private readonly ILogger _logger;

        public DefinedVersionOpStrategy(
            IFileSystem fileSystem,
            IFileContentCache fileCache,
            ILogger<DefinedVersionOpStrategy> logger)
        {
            _fileSystem = fileSystem;
            _fileCache = fileCache;
            _logger = logger;
        }

        public async Task<IVersionTemplate?> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var directory = item.Record.Path;
            var filePath = _fileSystem.Path.Combine(directory, Constants.VERSION_FILE_NAME);

            if (!_fileSystem.File.Exists(filePath))
            {
                return item.Parent is null
                    ? null
                    : await item.Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            string? versionString = null;

            try
            {
                var versionDocument = await _fileCache.GetOrLoadJsonContentAsync(filePath, cancellationToken);
                var rootElement = versionDocument.RootElement;
                versionString = rootElement
                    .GetProperty(Constants.VERSION_PROPERTY_NAME)
                    .GetString();
            }
            catch (JsonException exception)
            {
                _logger.LogWarning("Error in version.json file at {filePath}: {exception}", filePath, exception);
            }

            if (versionString is null
                || !VersionTemplate.TryParse(versionString, out var parsedVersion))
            {
                return item.Parent is null
                    ? null
                    : await item.Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            return parsedVersion;
        }
    }
}
