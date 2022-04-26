using System;

namespace c0ded0c.Core
{
    public static class PathExtensions
    {
        public static string GetRelativePath(this string? path, string? relativeTo)
        {
            if (string.IsNullOrWhiteSpace(path)
                || string.IsNullOrWhiteSpace(relativeTo))
            {
                return string.Empty;
            }

            Uri pathUri = new Uri(path);

            if (!IsDirectorySeparator(relativeTo[relativeTo.Length - 1]))
            {
                relativeTo += System.IO.Path.DirectorySeparatorChar;
            }

            Uri folderUri = new Uri(relativeTo);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString());
        }

        public static bool IsDirectorySeparator(this char character)
        {
            return character == System.IO.Path.DirectorySeparatorChar
                || character == System.IO.Path.AltDirectorySeparatorChar;
        }
    }
}
