using System.IO;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public static class ProjectStrategyHelper
    {
        public static string GetProjectFilePath(IItem project)
        {
            var projectPath = project.Record.Path;
            var sourceDirectoryPath = Path.Combine(projectPath, Constants.SOURCE_DIRECTORY_NAME);

            if (!Directory.Exists(sourceDirectoryPath))
            {
                sourceDirectoryPath = projectPath;
            }

            var projectFiles = Directory.GetFiles(sourceDirectoryPath, "*.*?proj", SearchOption.TopDirectoryOnly);
            return projectFiles.Length > 0 ? projectFiles[0] : string.Empty;
        }
    }
}
