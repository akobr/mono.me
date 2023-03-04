using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticDirectory : IDirectory
{
    private readonly IDirectory _executingDirectory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticDirectory(
        IDirectory executingDirectory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingDirectory = executingDirectory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public IDirectoryInfo CreateDirectory(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.CreateDirectory(path);
    }

    public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
    {
        _processor.Process(new object?[] { path, pathToTarget });
        return _executingDirectory.CreateSymbolicLink(path, pathToTarget);
    }

    public void Delete(string path)
    {
        _processor.Process(new object?[] { path });
        _executingDirectory.Delete(path);
    }

    public void Delete(string path, bool recursive)
    {
        _processor.Process(new object?[] { path, recursive });
        _executingDirectory.Delete(path, recursive);
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.EnumerateDirectories(path);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
    {
        _processor.Process(new object?[] { path, searchPattern });
        return _executingDirectory.EnumerateDirectories(path, searchPattern);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        _processor.Process(new object?[] { path, searchPattern, searchOption });
        return _executingDirectory.EnumerateDirectories(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        _processor.Process(new object?[] { path, searchPattern, enumerationOptions });
        return _executingDirectory.EnumerateDirectories(path, searchPattern, enumerationOptions);
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.EnumerateFiles(path);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
    {
        _processor.Process(new object?[] { path, searchPattern });
        return _executingDirectory.EnumerateFiles(path, searchPattern);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        _processor.Process(new object?[] { path, searchPattern, searchOption });
        return _executingDirectory.EnumerateFiles(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        _processor.Process(new object?[] { path, searchPattern, enumerationOptions });
        return _executingDirectory.EnumerateFiles(path, searchPattern, enumerationOptions);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.EnumerateFileSystemEntries(path);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern)
    {
        _processor.Process(new object?[] { path, searchPattern });
        return _executingDirectory.EnumerateFileSystemEntries(path, searchPattern);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
    {
        _processor.Process(new object?[] { path, searchPattern, searchOption });
        return _executingDirectory.EnumerateFileSystemEntries(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        _processor.Process(new object?[] { path, searchPattern, enumerationOptions });
        return _executingDirectory.EnumerateFileSystemEntries(path, searchPattern, enumerationOptions);
    }

    public bool Exists(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.Exists(path);
    }

    public DateTime GetCreationTime(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetCreationTime(path);
    }

    public DateTime GetCreationTimeUtc(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetCreationTimeUtc(path);
    }

    public string GetCurrentDirectory()
    {
        _processor.Process();
        return _executingDirectory.GetCurrentDirectory();
    }

    public string[] GetDirectories(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetDirectories(path);
    }

    public string[] GetDirectories(string path, string searchPattern)
    {
        _processor.Process(new object?[] { path, searchPattern });
        return _executingDirectory.GetDirectories(path, searchPattern);
    }

    public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        _processor.Process(new object?[] { path, searchPattern, searchOption });
        return _executingDirectory.GetDirectories(path, searchPattern, searchOption);
    }

    public string[] GetDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        _processor.Process(new object?[] { path, searchPattern, enumerationOptions });
        return _executingDirectory.GetDirectories(path, searchPattern, enumerationOptions);
    }

    public string GetDirectoryRoot(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetDirectoryRoot(path);
    }

    public string[] GetFiles(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetFiles(path);
    }

    public string[] GetFiles(string path, string searchPattern)
    {
        _processor.Process(new object?[] { path, searchPattern });
        return _executingDirectory.GetFiles(path, searchPattern);
    }

    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        _processor.Process(new object?[] { path, searchPattern, searchOption });
        return _executingDirectory.GetFiles(path, searchPattern, searchOption);
    }

    public string[] GetFiles(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        _processor.Process(new object?[] { path, searchPattern, enumerationOptions });
        return _executingDirectory.GetFiles(path, searchPattern, enumerationOptions);
    }

    public string[] GetFileSystemEntries(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetFileSystemEntries(path);
    }

    public string[] GetFileSystemEntries(string path, string searchPattern)
    {
        _processor.Process(new object?[] { path, searchPattern });
        return _executingDirectory.GetFileSystemEntries(path, searchPattern);
    }

    public string[] GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
    {
        _processor.Process(new object?[] { path, searchPattern, searchOption });
        return _executingDirectory.GetFileSystemEntries(path, searchPattern, searchOption);
    }

    public string[] GetFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        _processor.Process(new object?[] { path, searchPattern, enumerationOptions });
        return _executingDirectory.GetFileSystemEntries(path, searchPattern, enumerationOptions);
    }

    public DateTime GetLastAccessTime(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetLastAccessTime(path);
    }

    public DateTime GetLastAccessTimeUtc(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetLastAccessTimeUtc(path);
    }

    public DateTime GetLastWriteTime(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetLastWriteTime(path);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetLastWriteTimeUtc(path);
    }

    public string[] GetLogicalDrives()
    {
        _processor.Process();
        return _executingDirectory.GetLogicalDrives();
    }

    public IDirectoryInfo? GetParent(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingDirectory.GetParent(path);
    }

    public void Move(string sourceDirName, string destDirName)
    {
        _processor.Process(new object?[] { sourceDirName, destDirName });
        _executingDirectory.Move(sourceDirName, destDirName);
    }

    public IFileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget)
    {
        _processor.Process(new object?[] { linkPath, returnFinalTarget });
        return _executingDirectory.ResolveLinkTarget(linkPath, returnFinalTarget);
    }

    public void SetCreationTime(string path, DateTime creationTime)
    {
        _processor.Process(new object?[] { path, creationTime });
        _executingDirectory.SetCreationTime(path, creationTime);
    }

    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
    {
        _processor.Process(new object?[] { path, creationTimeUtc });
        _executingDirectory.SetCreationTimeUtc(path, creationTimeUtc);
    }

    public void SetCurrentDirectory(string path)
    {
        _processor.Process(new object?[] { path });
        _executingDirectory.SetCurrentDirectory(path);
    }

    public void SetLastAccessTime(string path, DateTime lastAccessTime)
    {
        _processor.Process(new object?[] { path, lastAccessTime });
        _executingDirectory.SetLastAccessTime(path, lastAccessTime);
    }

    public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
    {
        _processor.Process(new object?[] { path, lastAccessTimeUtc });
        _executingDirectory.SetLastAccessTimeUtc(path, lastAccessTimeUtc);
    }

    public void SetLastWriteTime(string path, DateTime lastWriteTime)
    {
        _processor.Process(new object?[] { path, lastWriteTime });
        _executingDirectory.SetLastWriteTime(path, lastWriteTime);
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        _processor.Process(new object?[] { path, lastWriteTimeUtc });
        _executingDirectory.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
    }
}
