using System;
using System.IO;

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
                relativeTo += Path.DirectorySeparatorChar;
            }

            var folderUri = new Uri(relativeTo);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString());
        }

        public static bool IsDirectorySeparator(this char character)
        {
            return character == Path.DirectorySeparatorChar
                   || character == Path.AltDirectorySeparatorChar;
        }

        public static string NormalizePath(this string? path)
        {
            return path?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar) ?? string.Empty;
        }
    }
}
