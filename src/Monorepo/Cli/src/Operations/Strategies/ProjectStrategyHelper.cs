using System.IO;
using System.IO.Abstractions;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public static class ProjectStrategyHelper
    {
        public static string GetProjectFilePath(IItem project, IFileSystem fileSystem)
        {
            var projectPath = project.Record.Path;
            var sourceDirectoryPath = fileSystem.Path.Combine(projectPath, Constants.SOURCE_DIRECTORY_NAME);

            if (!fileSystem.Directory.Exists(sourceDirectoryPath))
            {
                sourceDirectoryPath = projectPath;
            }

            var projectFiles = fileSystem.Directory.GetFiles(sourceDirectoryPath, "*.*?proj", SearchOption.TopDirectoryOnly);
            return projectFiles.Length > 0 ? projectFiles[0] : string.Empty;
        }
    }
}
