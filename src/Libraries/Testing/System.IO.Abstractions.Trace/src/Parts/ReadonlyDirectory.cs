using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class ReadonlyDirectory : IDirectory
{
    private readonly IDirectory _executingDirectory;

    public ReadonlyDirectory(IDirectory executingDirectory, IFileSystem fileSystem)
    {
        _executingDirectory = executingDirectory;
        FileSystem = fileSystem;
    }

    public IFileSystem FileSystem { get; }

    public IDirectoryInfo CreateDirectory(string path)
    {
        // TODO: simulate it by in memory map
        return new DirectoryInfoWrapper(FileSystem, new DirectoryInfo(path));
    }

    public IDirectoryInfo CreateDirectory(string path, UnixFileMode unixCreateMode)
    {
        throw new NotImplementedException();
    }

    public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
    {
        // TODO: simulate it by in memory map
        return new FileInfoWrapper(FileSystem, new FileInfo(path));
    }

    public IDirectoryInfo CreateTempSubdirectory(string? prefix = null)
    {
        throw new NotImplementedException();
    }

    public void Delete(string path)
    {
        // no operation
    }

    public void Delete(string path, bool recursive)
    {
        // no operation
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        return _executingDirectory.EnumerateDirectories(path);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
    {
        return _executingDirectory.EnumerateDirectories(path, searchPattern);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        return _executingDirectory.EnumerateDirectories(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        return _executingDirectory.EnumerateDirectories(path, searchPattern, enumerationOptions);
    }

    public IEnumerable<string> EnumerateFiles(string path)
    {
        return _executingDirectory.EnumerateFiles(path);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
    {
        return _executingDirectory.EnumerateFiles(path, searchPattern);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return _executingDirectory.EnumerateFiles(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        return _executingDirectory.EnumerateFiles(path, searchPattern, enumerationOptions);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path)
    {
        return _executingDirectory.EnumerateFileSystemEntries(path);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern)
    {
        return _executingDirectory.EnumerateFileSystemEntries(path, searchPattern);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
    {
        return _executingDirectory.EnumerateFileSystemEntries(path, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        return _executingDirectory.EnumerateFileSystemEntries(path, searchPattern, enumerationOptions);
    }

    public bool Exists(string? path)
    {
        return _executingDirectory.Exists(path);
    }

    public DateTime GetCreationTime(string path)
    {
        return _executingDirectory.GetCreationTime(path);
    }

    public DateTime GetCreationTimeUtc(string path)
    {
        return _executingDirectory.GetCreationTimeUtc(path);
    }

    public string GetCurrentDirectory()
    {
        return _executingDirectory.GetCurrentDirectory();
    }

    public string[] GetDirectories(string path)
    {
        return _executingDirectory.GetDirectories(path);
    }

    public string[] GetDirectories(string path, string searchPattern)
    {
        return _executingDirectory.GetDirectories(path, searchPattern);
    }

    public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        return _executingDirectory.GetDirectories(path, searchPattern, searchOption);
    }

    public string[] GetDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        return _executingDirectory.GetDirectories(path, searchPattern, enumerationOptions);
    }

    public string GetDirectoryRoot(string path)
    {
        return _executingDirectory.GetDirectoryRoot(path);
    }

    public string[] GetFiles(string path)
    {
        return _executingDirectory.GetFiles(path);
    }

    public string[] GetFiles(string path, string searchPattern)
    {
        return _executingDirectory.GetFiles(path, searchPattern);
    }

    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return _executingDirectory.GetFiles(path, searchPattern, searchOption);
    }

    public string[] GetFiles(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        return _executingDirectory.GetFiles(path, searchPattern, enumerationOptions);
    }

    public string[] GetFileSystemEntries(string path)
    {
        return _executingDirectory.GetFileSystemEntries(path);
    }

    public string[] GetFileSystemEntries(string path, string searchPattern)
    {
        return _executingDirectory.GetFileSystemEntries(path, searchPattern);
    }

    public string[] GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
    {
        return _executingDirectory.GetFileSystemEntries(path, searchPattern, searchOption);
    }

    public string[] GetFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions)
    {
        return _executingDirectory.GetFileSystemEntries(path, searchPattern, enumerationOptions);
    }

    public DateTime GetLastAccessTime(string path)
    {
        return _executingDirectory.GetLastAccessTime(path);
    }

    public DateTime GetLastAccessTimeUtc(string path)
    {
        return _executingDirectory.GetLastAccessTimeUtc(path);
    }

    public DateTime GetLastWriteTime(string path)
    {
        return _executingDirectory.GetLastWriteTime(path);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        return _executingDirectory.GetLastWriteTimeUtc(path);
    }

    public string[] GetLogicalDrives()
    {
        return _executingDirectory.GetLogicalDrives();
    }

    public IDirectoryInfo? GetParent(string path)
    {
        return _executingDirectory.GetParent(path);
    }

    public void Move(string sourceDirName, string destDirName)
    {
        // TODO: simulate it by in memory map
    }

    public IFileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget)
    {
        return _executingDirectory.ResolveLinkTarget(linkPath, returnFinalTarget);
    }

    public void SetCreationTime(string path, DateTime creationTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCurrentDirectory(string path)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastAccessTime(string path, DateTime lastAccessTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastWriteTime(string path, DateTime lastWriteTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        // TODO: simulate it by in memory map
    }
}
