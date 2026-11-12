using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The files of a set's folder, read through the file system the tool is given. The documents are named and read as
// in the folder of an export (ViewArchiveReader), so a set and an export read alike; hidden paths are skipped.
public static class DataSetFolder
{
    // Limits sized for sets: a set is ours, so the limits of an upload do not apply, but one document is still one
    // Cosmos DB item.
    public static readonly ViewTransferOptions ReaderOptions = new()
    {
        MaxArchiveEntries = 1_000_000,
        MaxExtractedBytes = 2L * 1024 * 1024 * 1024,
        MaxDocumentBytes = ViewTransferOptions.MaxAllowedDocumentBytes,
    };

    // Every file below the folder in ordinal order of its path, without the skipped paths: a skipped folder is not
    // entered.
    public static IReadOnlyList<DataSetFile> GetFiles(IFileSystem fileSystem, string folder)
    {
        var files = new List<DataSetFile>();
        Collect(fileSystem, folder, string.Empty, files);
        files.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return files;
    }

    // The files for the reader: every file but the manifest and annotations.jsonl at the root of the set, which are
    // not documents and would count as ignored.
    public static IReadOnlyList<ViewArchiveEntry> GetEntries(IFileSystem fileSystem, string folder)
    {
        return GetFiles(fileSystem, folder)
            .Where(file => file.Path is not DataSetManifest.FileName and not DataSetAnnotationLinesReader.FileName)
            .Select(file => new ViewArchiveEntry(
                file.Path,
                fileSystem.FileInfo.New(file.FullPath).Length,
                () => fileSystem.File.OpenRead(file.FullPath)))
            .ToList();
    }

    // DataSetException when the folder is beyond the limits of the reader.
    public static ViewArchiveDocuments ReadDocuments(IFileSystem fileSystem, string folder)
    {
        try
        {
            return ViewArchiveReader.Read(GetEntries(fileSystem, folder), ReaderOptions);
        }
        catch (InvalidInputException exception)
        {
            throw new DataSetException($"The set '{folder}' cannot be read: {exception.Message}", exception);
        }
    }

    // SHA-256 (lower-case hex) over the path and the content of every file of the set in path order, so any change of
    // a file, a new file or a removed one changes it. Text files count with LF line ends, so a checkout with CRLF
    // (core.autocrlf) hashes as one with LF.
    public static string ComputeHash(IFileSystem fileSystem, string folder)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var file in GetFiles(fileSystem, folder))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.Path));
            hash.AppendData([0]);
            hash.AppendData(SHA256.HashData(NormalizeLineEnds(fileSystem.File.ReadAllBytes(file.FullPath))));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // CRLF becomes LF in a text file; a binary file stays as it is.
    public static byte[] NormalizeLineEnds(byte[] content)
    {
        // Git's test of a binary file: a NUL byte among the first 8 000 bytes
        const int textProbeBytes = 8000;

        if (content.AsSpan(0, Math.Min(content.Length, textProbeBytes)).Contains((byte)0)
            || content.AsSpan().IndexOf("\r\n"u8) < 0)
        {
            return content;
        }

        var normalized = new byte[content.Length];
        var length = 0;

        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
            {
                continue;
            }

            normalized[length++] = content[index];
        }

        return normalized[..length];
    }

    private static void Collect(IFileSystem fileSystem, string directory, string prefix, List<DataSetFile> files)
    {
        foreach (var file in fileSystem.Directory.EnumerateFiles(directory))
        {
            var path = prefix + fileSystem.Path.GetFileName(file);

            if (!ViewArchiveNames.IsSkippedPath(path))
            {
                files.Add(new DataSetFile(path, file));
            }
        }

        foreach (var child in fileSystem.Directory.EnumerateDirectories(directory))
        {
            var path = prefix + fileSystem.Path.GetFileName(child);

            if (!ViewArchiveNames.IsSkippedPath(path))
            {
                Collect(fileSystem, child, path + "/", files);
            }
        }
    }
}
