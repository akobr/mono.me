using System;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.Versioning;

namespace _42.Platform.Cli.Output;

// A file that its owner alone can read and write from the moment it exists, for content that may hold secrets. The
// text goes into a new file next to the target, created with mode 0600 (FileMode.CreateNew, so an existing file or a
// symbolic link at that name is never opened), which then replaces the target. The path therefore never names a file
// with wider permissions while the text is written, and a reader that had opened an earlier file at the path keeps
// that file and never sees the text. A symbolic link at the path is replaced, not followed.
[UnsupportedOSPlatform("windows")]
internal static class OwnerOnlyFile
{
    public const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void WriteAllText(IFileSystem fileSystem, string path, string text)
    {
        var target = fileSystem.Path.GetFullPath(path);
        var directory = fileSystem.Path.GetDirectoryName(target) ?? throw new IOException($"'{path}' is not a path of a file.");
        var temporary = fileSystem.Path.Combine(directory, $".{fileSystem.Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            UnixCreateMode = Mode,
        };

        try
        {
            using (var stream = fileSystem.FileStream.New(temporary, options))
            {
                stream.Write(PlainOutputExtensions.Utf8.GetBytes(text));
            }

            fileSystem.File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            DeleteIfLeft(fileSystem, temporary);
        }
    }

    // After a failed write or move; a file that cannot be deleted either is left, as the failure is reported anyway.
    private static void DeleteIfLeft(IFileSystem fileSystem, string temporary)
    {
        try
        {
            if (fileSystem.File.Exists(temporary))
            {
                fileSystem.File.Delete(temporary);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the original exception is the one to report
        }
    }
}
