using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Testing.System.IO.Abstractions.Wrappers;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class ReadonlyFile : IFile
{
    private readonly IFile _executingFile;

    public ReadonlyFile(IFile executingFile, IFileSystem fileSystem)
    {
        _executingFile = executingFile;
        FileSystem = fileSystem;
    }

    public ReadonlyFile(IFileSystem fileSystem)
        : this(new FileWrapper(fileSystem), fileSystem)
    {
        // no operation
    }

    public IFileSystem FileSystem { get; }

    public Task AppendAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task AppendAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task AppendAllTextAsync(
        string path,
        string? contents,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task AppendAllTextAsync(
        string path,
        string? contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadAllLinesAsync(path, cancellationToken);
    }

    public Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadAllLinesAsync(path, encoding, cancellationToken);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadAllTextAsync(path, cancellationToken);
    }

    public Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadAllTextAsync(path, encoding, cancellationToken);
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task WriteAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task WriteAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task WriteAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task WriteAllTextAsync(
        string path,
        string? contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public void AppendAllLines(string path, IEnumerable<string> contents)
    {
        // TODO: simulate it by in memory map
    }

    public void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
    }

    public void AppendAllText(string path, string? contents)
    {
        // TODO: simulate it by in memory map
    }

    public void AppendAllText(string path, string? contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
    }

    public StreamWriter AppendText(string path)
    {
        // TODO: simulate it by in memory map
        return new StreamWriter(new MemoryStream());
    }

    public void Copy(string sourceFileName, string destFileName)
    {
        // TODO: simulate it by in memory map
    }

    public void Copy(string sourceFileName, string destFileName, bool overwrite)
    {
        // TODO: simulate it by in memory map
    }

    public FileSystemStream Create(string path)
    {
        // TODO: simulate it by in memory map
        return new InMemoryStream();
    }

    public FileSystemStream Create(string path, int bufferSize)
    {
        // TODO: simulate it by in memory map
        return new InMemoryStream();
    }

    public FileSystemStream Create(string path, int bufferSize, FileOptions options)
    {
        // TODO: simulate it by in memory map
        return new InMemoryStream();
    }

    public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
    {
        // TODO: simulate it by in memory map
        return new FileInfoWrapper(FileSystem, new FileInfo(path));
    }

    public StreamWriter CreateText(string path)
    {
        // TODO: simulate it by in memory map
        return new StreamWriter(new MemoryStream());
    }

    [SupportedOSPlatform("windows")]
    public void Decrypt(string path)
    {
        _executingFile.Decrypt(path);
    }

    public void Delete(string path)
    {
        // TODO: simulate it by in memory map
    }

    [SupportedOSPlatform("windows")]
    public void Encrypt(string path)
    {
        // no operation
    }

    public bool Exists(string? path)
    {
        return _executingFile.Exists(path);
    }

    public FileAttributes GetAttributes(string path)
    {
        return _executingFile.GetAttributes(path);
    }

    public DateTime GetCreationTime(string path)
    {
        return _executingFile.GetCreationTime(path);
    }

    public DateTime GetCreationTimeUtc(string path)
    {
        return _executingFile.GetCreationTimeUtc(path);
    }

    public DateTime GetLastAccessTime(string path)
    {
        return _executingFile.GetLastAccessTime(path);
    }

    public DateTime GetLastAccessTimeUtc(string path)
    {
        return _executingFile.GetLastAccessTimeUtc(path);
    }

    public DateTime GetLastWriteTime(string path)
    {
        return _executingFile.GetLastWriteTime(path);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        return _executingFile.GetLastWriteTimeUtc(path);
    }

    public void Move(string sourceFileName, string destFileName)
    {
        // TODO: simulate it by in memory map
    }

    public void Move(string sourceFileName, string destFileName, bool overwrite)
    {
        // TODO: simulate it by in memory map
    }

    public FileSystemStream Open(string path, FileMode mode)
    {
        return mode switch
        {
            FileMode.Open => _executingFile.Open(path, mode, FileAccess.Read),
            FileMode.OpenOrCreate when _executingFile.Exists(path) => _executingFile.Open(path, FileMode.Open, FileAccess.Read),
            _ => new InMemoryStream(),
        };
    }

    public FileSystemStream Open(string path, FileMode mode, FileAccess access)
    {
        if (access != FileAccess.Read)
        {
            return new InMemoryStream();
        }

        return mode switch
        {
            FileMode.Open => _executingFile.Open(path, mode, FileAccess.Read),
            FileMode.OpenOrCreate when _executingFile.Exists(path) => _executingFile.Open(path, FileMode.Open, FileAccess.Read),
            _ => new InMemoryStream(),
        };
    }

    public FileSystemStream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        // TODO: simulate it by in memory map
        return new InMemoryStream();
    }

    public FileSystemStream Open(string path, FileStreamOptions options)
    {
        // TODO: simulate it by in memory map
        return new InMemoryStream();
    }

    public FileSystemStream OpenRead(string path)
    {
        return _executingFile.OpenRead(path);
    }

    public StreamReader OpenText(string path)
    {
        return _executingFile.OpenText(path);
    }

    public FileSystemStream OpenWrite(string path)
    {
        // TODO: simulate it by in memory map
        return new InMemoryStream();
    }

    public byte[] ReadAllBytes(string path)
    {
        return _executingFile.ReadAllBytes(path);
    }

    public string[] ReadAllLines(string path)
    {
        return _executingFile.ReadAllLines(path);
    }

    public string[] ReadAllLines(string path, Encoding encoding)
    {
        return _executingFile.ReadAllLines(path, encoding);
    }

    public string ReadAllText(string path)
    {
        return _executingFile.ReadAllText(path);
    }

    public string ReadAllText(string path, Encoding encoding)
    {
        return _executingFile.ReadAllText(path, encoding);
    }

    public IEnumerable<string> ReadLines(string path)
    {
        return _executingFile.ReadLines(path);
    }

    public IEnumerable<string> ReadLines(string path, Encoding encoding)
    {
        return _executingFile.ReadLines(path, encoding);
    }

    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        // TODO: simulate it by in memory map
    }

    public void Replace(
        string sourceFileName,
        string destinationFileName,
        string? destinationBackupFileName,
        bool ignoreMetadataErrors)
    {
        // TODO: simulate it by in memory map
    }

    public IFileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget)
    {
        // TODO: simulate it by in memory map
        return new FileInfoWrapper(FileSystem, new FileInfo(linkPath));
    }

    public void SetAttributes(string path, FileAttributes fileAttributes)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTime(string path, DateTime creationTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
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

    public void WriteAllBytes(string path, byte[] bytes)
    {
        // TODO: simulate it by in memory map
    }

    public void WriteAllLines(string path, string[] contents)
    {
        // TODO: simulate it by in memory map
    }

    public void WriteAllLines(string path, IEnumerable<string> contents)
    {
        // TODO: simulate it by in memory map
    }

    public void WriteAllLines(string path, string[] contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
    }

    public void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
    }

    public void WriteAllText(string path, string? contents)
    {
        // TODO: simulate it by in memory map
    }

    public void WriteAllText(string path, string? contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
    }
}
