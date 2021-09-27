using System;

namespace _42.Monorepo.Cli.Extensions
{
    public static class PathExtensions
    {
        public static string GetRelativePath(this string path, string relativeTo)
        {
            if (string.IsNullOrWhiteSpace(path)
                || string.IsNullOrWhiteSpace(relativeTo))
            {
                return string.Empty;
            }

            var pathUri = new Uri(path);

            if (!IsDirectorySeparator(relativeTo[^1]))
            {
                relativeTo += System.IO.Path.DirectorySeparatorChar;
            }

            var folderUri = new Uri(relativeTo);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString());
        }

        public static bool IsDirectorySeparator(this char character)
        {
            return character == System.IO.Path.DirectorySeparatorChar
                   || character == System.IO.Path.AltDirectorySeparatorChar;
        }
    }
}
