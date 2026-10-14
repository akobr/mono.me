using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Services;

/// <summary>
/// Zips of configuration documents on the local disk: a folder packed for an import, an export unpacked into a folder.
/// </summary>
public static class ConfigurationArchives
{
    private const string GitFolder = ".git";

    /// <summary>
    /// Packs every *.json file below the folder, in subfolders too and without .git folders, into a zip in memory.
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
            var files = fileSystem.Directory
                .EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(path => (Path: path, Relative: fileSystem.Path.GetRelativePath(root, path).Replace('\\', '/')))
                .Where(file => !file.Relative.Split('/').Any(segment => segment.Equals(GitFolder, StringComparison.OrdinalIgnoreCase)))
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

            if (!(target + (isDirectory ? fileSystem.Path.DirectorySeparatorChar : string.Empty)).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
}
