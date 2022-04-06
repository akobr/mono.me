using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Versioning;
using Microsoft.Extensions.Logging;
using Semver;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class DefinedVersionOpStrategy : IOpStrategy<IVersionTemplate?>
    {
        private readonly IFileContentCache fileCache;
        private readonly ILogger<DefinedVersionOpStrategy> logger;

        public DefinedVersionOpStrategy(
            IFileContentCache fileCache,
            ILogger<DefinedVersionOpStrategy> logger)
        {
            this.fileCache = fileCache;
            this.logger = logger;
        }

        public async Task<IVersionTemplate?> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var directory = item.Record.Path;
            var filePath = Path.Combine(directory, Constants.VERSION_FILE_NAME);

            if (!File.Exists(filePath))
            {
                return item.Parent is null
                    ? null
                    : await item.Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            string? versionString = null;

            try
            {
                var versionDocument = await fileCache.GetOrLoadJsonContentAsync(filePath, cancellationToken);
                var rootElement = versionDocument.RootElement;
                versionString = rootElement
                    .GetProperty(Constants.VERSION_PROPERTY_NAME)
                    .GetString();
            }
            catch (JsonException exception)
            {
                logger.LogWarning("Error in version.json file at {filePath}: {exception}", filePath, exception);
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
