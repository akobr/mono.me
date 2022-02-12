using System.Collections.Generic;
using System.Linq;
using System.Text;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Extensions
{
    public static class RecordExtensions
    {
        public static string GetRepoRelativePath(IRecord @this)
        {
            return @this.Identifier.Humanized;
        }

        public static string GetRelativePathToRootDirectory(IRecord @this)
        {
            var pathStack = new Stack<string>();
            var record = @this;

            while (record is not null
                   && record.Type >= RecordType.TopWorkstead)
            {
                pathStack.Push(record.Name);
                record = record.Parent;
            }

            var path = pathStack.Aggregate(
                new StringBuilder(),
                (path, dirName) =>
                {
                    path.Append(dirName);
                    path.Append(Constants.DIRECTORY_SEPARATOR);
                    return path;
                });

            if (path.Length > 0)
            {
                path.Remove(path.Length - 1, 1);
            }

            return path.ToString();
        }
    }
}
