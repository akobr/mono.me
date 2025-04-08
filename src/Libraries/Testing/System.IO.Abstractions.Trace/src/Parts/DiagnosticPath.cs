using System;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticPath : IPath
{
    private readonly IPath _executingPath;
    private readonly IFileSystemTracer _processor;

    public DiagnosticPath(
        IPath executingPath,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingPath = executingPath;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public char AltDirectorySeparatorChar
    {
        get
        {
            _processor.Process();
            return _executingPath.AltDirectorySeparatorChar;
        }
    }

    public char DirectorySeparatorChar
    {
        get
        {
            _processor.Process();
            return _executingPath.DirectorySeparatorChar;
        }
    }

    public char PathSeparator
    {
        get
        {
            _processor.Process();
            return _executingPath.PathSeparator;
        }
    }

    public char VolumeSeparatorChar
    {
        get
        {
            _processor.Process();
            return _executingPath.VolumeSeparatorChar;
        }
    }

    public string? ChangeExtension(string? path, string? extension)
    {
        _processor.Process(new object?[] { path, extension });
        return _executingPath.ChangeExtension(path, extension);
    }

    public string Combine(string path1, string path2)
    {
        _processor.Process(new object?[] { path1, path2 });
        return _executingPath.Combine(path1, path2);
    }

    public string Combine(string path1, string path2, string path3)
    {
        _processor.Process(new object?[] { path1, path2, path3 });
        return _executingPath.Combine(path1, path2, path3);
    }

    public string Combine(string path1, string path2, string path3, string path4)
    {
        _processor.Process(new object?[] { path1, path2, path3, path4 });
        return _executingPath.Combine(path1, path2, path3, path4);
    }

    public string Combine(params string[] paths)
    {
        _processor.Process(new object?[] { paths });
        return _executingPath.Combine(paths);
    }

    public string Combine(scoped ReadOnlySpan<string> paths)
    {
        _processor.Process(new object?[] { paths.ToArray() });
        return _executingPath.Combine(paths);
    }

    public bool EndsInDirectorySeparator(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.EndsInDirectorySeparator(path);
    }

    public bool EndsInDirectorySeparator(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.EndsInDirectorySeparator(path);
    }

    public bool Exists(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.Exists(path);
    }

    public ReadOnlySpan<char> GetDirectoryName(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.GetDirectoryName(path);
    }

    public string? GetDirectoryName(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.GetDirectoryName(path);
    }

    public ReadOnlySpan<char> GetExtension(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.GetExtension(path);
    }

    public string? GetExtension(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.GetExtension(path);
    }

    public ReadOnlySpan<char> GetFileName(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.GetFileName(path);
    }

    public string? GetFileName(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.GetFileName(path);
    }

    public ReadOnlySpan<char> GetFileNameWithoutExtension(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.GetFileNameWithoutExtension(path);
    }

    public string? GetFileNameWithoutExtension(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.GetFileNameWithoutExtension(path);
    }

    public string GetFullPath(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.GetFullPath(path);
    }

    public string GetFullPath(string path, string basePath)
    {
        _processor.Process(new object?[] { path, basePath });
        return _executingPath.GetFullPath(path, basePath);
    }

    public char[] GetInvalidFileNameChars()
    {
        _processor.Process();
        return _executingPath.GetInvalidFileNameChars();
    }

    public char[] GetInvalidPathChars()
    {
        _processor.Process();
        return _executingPath.GetInvalidPathChars();
    }

    public ReadOnlySpan<char> GetPathRoot(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.GetPathRoot(path);
    }

    public string? GetPathRoot(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.GetPathRoot(path);
    }

    public string GetRandomFileName()
    {
        _processor.Process();
        return _executingPath.GetRandomFileName();
    }

    public string GetRelativePath(string relativeTo, string path)
    {
        _processor.Process(new object?[] { relativeTo, path });
        return _executingPath.GetRelativePath(relativeTo, path);
    }

    public string GetTempFileName()
    {
        _processor.Process();
        return _executingPath.GetTempFileName();
    }

    public string GetTempPath()
    {
        _processor.Process();
        return _executingPath.GetTempPath();
    }

    public bool HasExtension(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.HasExtension(path);
    }

    public bool HasExtension(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.HasExtension(path);
    }

    public bool IsPathFullyQualified(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.IsPathFullyQualified(path);
    }

    public bool IsPathFullyQualified(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.IsPathFullyQualified(path);
    }

    public bool IsPathRooted(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.IsPathRooted(path);
    }

    public bool IsPathRooted(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.IsPathRooted(path);
    }

    public string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2)
    {
        _processor.Process(new object?[] { path1.ToArray(), path2.ToArray() });
        return _executingPath.Join(path1, path2);
    }

    public string Join(ReadOnlySpan<char> path1, ReadOnlySpan<char> path2, ReadOnlySpan<char> path3)
    {
        _processor.Process(new object?[] { path1.ToArray(), path2.ToArray(), path3.ToArray() });
        return _executingPath.Join(path1, path2, path3);
    }

    public bool TryJoin(
        ReadOnlySpan<char> path1,
        ReadOnlySpan<char> path2,
        Span<char> destination,
        out int charsWritten)
    {
        _processor.Process(new object?[] { path1.ToArray(), path2.ToArray(), destination.ToArray() });
        return _executingPath.TryJoin(path1, path2, destination, out charsWritten);
    }

    public bool TryJoin(
        ReadOnlySpan<char> path1,
        ReadOnlySpan<char> path2,
        ReadOnlySpan<char> path3,
        Span<char> destination,
        out int charsWritten)
    {
        _processor.Process(new object?[] { path1.ToArray(), path2.ToArray(), path3.ToArray(), destination.ToArray() });
        return _executingPath.TryJoin(path1, path2, path3, destination, out charsWritten);
    }

    public string Join(string? path1, string? path2)
    {
        _processor.Process(new object?[] { path1, path2 });
        return _executingPath.Join(path1, path2);
    }

    public string Join(string? path1, string? path2, string? path3)
    {
        _processor.Process(new object?[] { path1, path2, path3 });
        return _executingPath.Join(path1, path2, path3);
    }

    public string Join(params string?[] paths)
    {
        _processor.Process(new object?[] { paths });
        return _executingPath.Join(paths);
    }

    public string Join(scoped ReadOnlySpan<string?> paths)
    {
        _processor.Process(new object?[] { paths.ToArray() });
        return _executingPath.Join(paths);
    }

    public ReadOnlySpan<char> TrimEndingDirectorySeparator(ReadOnlySpan<char> path)
    {
        _processor.Process(new object?[] { path.ToArray() });
        return _executingPath.TrimEndingDirectorySeparator(path);
    }

    public string TrimEndingDirectorySeparator(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingPath.TrimEndingDirectorySeparator(path);
    }

    public string Join(
        ReadOnlySpan<char> path1,
        ReadOnlySpan<char> path2,
        ReadOnlySpan<char> path3,
        ReadOnlySpan<char> path4)
    {
        _processor.Process(new object?[] { path1.ToArray(), path2.ToArray(), path3.ToArray(), path4.ToArray() });
        return _executingPath.Join(path1, path2, path3, path4);
    }

    public string Join(string? path1, string? path2, string? path3, string? path4)
    {
        _processor.Process(new object?[] { path1, path2, path3, path4 });
        return _executingPath.Join(path1, path2, path3, path4);
    }
}
