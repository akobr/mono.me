using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Services;

/// <summary>
/// Zips of the documents of a view on the local disk: a folder packed for an import, an export unpacked into a folder.
/// </summary>
public static class ViewArchives
{
    private const string JsonExtension = ".json";
    private const string MacOsFolder = "__MACOSX";

    // Paths differ only by case on Windows; elsewhere a case mismatch is another folder.
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Packs every *.json file below the folder, in subfolders too, into a zip in memory. Hidden folders and files
    /// (.git, .github, .vscode, ._*) and a __MACOSX folder are left out, as the server skips them (<see cref="IsSkippedPath"/>).
    /// Entry names are the paths relative to the folder, with '/'.
    /// </summary>
    /// <returns>The zip, positioned at its start, and the number of packed files.</returns>
    public static (MemoryStream Archive, int FileCount) PackFolder(IFileSystem fileSystem, string folder)
    {
        var root = fileSystem.Path.GetFullPath(folder);
        var archive = new MemoryStream();
        var count = 0;

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = EnumerateDocuments(fileSystem, root, string.Empty)
                .OrderBy(file => file.Relative, StringComparer.Ordinal);

            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Relative, CompressionLevel.Optimal);
                using var target = entry.Open();
                using var source = fileSystem.File.OpenRead(file.Path);
                source.CopyTo(target);
                count++;
            }
        }

        archive.Position = 0;
        return (archive, count);
    }

    /// <summary>
    /// Whether the server's archive reader passes over a path (with '/' separators) without counting it: any hidden folder
    /// or file (a segment starting with '.') and the __MACOSX folder of a zip. The same rule as ViewArchiveNames.IsSkippedPath.
    /// </summary>
    public static bool IsSkippedPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 0
               || segments.Any(segment => segment.StartsWith('.') && segment != ".")
               || segments[0].Equals(MacOsFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unpacks a zip into a folder that is missing or empty. Entries that would land outside the folder are refused.
    /// </summary>
    /// <returns>The number of written files.</returns>
    public static async Task<int> ExtractAsync(IFileSystem fileSystem, Stream archive, string folder)
    {
        var root = fileSystem.Path.GetFullPath(folder);

        if (fileSystem.File.Exists(root))
        {
            throw new InvalidOperationException($"'{folder}' is a file, not a folder.");
        }

        if (fileSystem.Directory.Exists(root) && fileSystem.Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException($"The folder '{folder}' is not empty.");
        }

        // a zip is read through its central directory at the end, so the download is buffered first
        using var buffer = new MemoryStream();
        await archive.CopyToAsync(buffer);
        buffer.Position = 0;

        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var prefix = root.EndsWith(fileSystem.Path.DirectorySeparatorChar) ? root : root + fileSystem.Path.DirectorySeparatorChar;
        var count = 0;

        foreach (var entry in zip.Entries)
        {
            var target = fileSystem.Path.GetFullPath(fileSystem.Path.Combine(root, entry.FullName));
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

            if (!(target + (isDirectory ? fileSystem.Path.DirectorySeparatorChar : string.Empty)).StartsWith(prefix, PathComparison))
            {
                throw new InvalidOperationException($"The archive entry '{entry.FullName}' points outside the folder.");
            }

            if (isDirectory)
            {
                fileSystem.Directory.CreateDirectory(target);
                continue;
            }

            fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(target)!);

            using (var source = entry.Open())
            using (var file = fileSystem.File.Create(target))
            {
                await source.CopyToAsync(file);
            }

            count++;
        }

        return count;
    }

    // The *.json files below a folder (the extension in any case, as the server reads it), without descending into the
    // skipped folders, so a large .git is never walked.
    private static IEnumerable<(string Path, string Relative)> EnumerateDocuments(IFileSystem fileSystem, string folder, string relativeFolder)
    {
        foreach (var path in fileSystem.Directory.EnumerateFiles(folder))
        {
            var relative = relativeFolder + fileSystem.Path.GetFileName(path);

            if (relative.EndsWith(JsonExtension, StringComparison.OrdinalIgnoreCase) && !IsSkippedPath(relative))
            {
                yield return (path, relative);
            }
        }

        foreach (var subfolder in fileSystem.Directory.EnumerateDirectories(folder))
        {
            var relative = relativeFolder + fileSystem.Path.GetFileName(subfolder) + "/";

            if (IsSkippedPath(relative))
            {
                continue;
            }

            foreach (var file in EnumerateDocuments(fileSystem, subfolder, relative))
            {
                yield return file;
            }
        }
    }
}
