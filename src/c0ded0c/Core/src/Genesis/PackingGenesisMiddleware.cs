using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using c0ded0c.Core.Configuration;

namespace c0ded0c.Core.Genesis
{
    public class PackingGenesisMiddleware : IGenesisMiddleware, IInitialisableWithProperties
    {
        private ImmutableDictionary<string, string> properties;

        public PackingGenesisMiddleware()
        {
            properties = ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        }

        public void Initialise(IImmutableDictionary<string, string> configuration)
        {
            properties = properties.SetItems(configuration);
        }

        public async Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace, GenesisAsyncDelegate next)
        {
            if (properties.IsSet(PropertyNames.IsPacked))
            {
                await Task.Run(() =>
                {
                    string outputDirectory = properties.Get(PropertyNames.OutputDirectory, Constants.SPARE_OUTPUT_DIRECTORY);
                    Directory.CreateDirectory(outputDirectory);

                    ZipFile.CreateFromDirectory(
                        Path.Combine(
                            properties.Get(PropertyNames.WorkingDirectory, Constants.SPARE_WORKING_DIRECTORY),
                            properties.Get(PropertyNames.RunName, Constants.SPARE_RUN_NAME)),
                        GetPackageFilePath(outputDirectory));
                });
            }

            return await next(workspace);
        }

        private string GetPackageFilePath(string directory)
        {
            return Path.Combine(directory, GetPackageFileName());
        }

        private string GetPackageFileName()
        {
            string name = properties.Get(PropertyNames.RunName, Constants.SPARE_RUN_NAME);
            if (properties.TryGetValue(PropertyNames.Version, out string? version))
            {
                name += $".{version}";
            }

            return name + Constants.PACKAGE_EXTENSION;
        }
    }
}
