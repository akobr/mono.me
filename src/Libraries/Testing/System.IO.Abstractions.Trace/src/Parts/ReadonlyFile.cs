using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Testing.System.IO.Abstractions.Wrappers;
using Microsoft.Win32.SafeHandles;

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

    public void AppendAllText(string path, ReadOnlySpan<char> contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
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

    public Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents,
        CancellationToken cancellationToken = new CancellationToken())
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding,
        CancellationToken cancellationToken = new CancellationToken())
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

    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadLinesAsync(path, cancellationToken);
    }

    public IAsyncEnumerable<string> ReadLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        return _executingFile.ReadLinesAsync(path, encoding, cancellationToken);
    }

    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        // TODO: simulate it by in memory map
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = new CancellationToken())
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

    public void WriteAllText(string path, ReadOnlySpan<char> contents, Encoding encoding)
    {
        // TODO: simulate it by in memory map
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

    public Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents,
        CancellationToken cancellationToken = new CancellationToken())
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding,
        CancellationToken cancellationToken = new CancellationToken())
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public void AppendAllBytes(string path, byte[] bytes)
    {
        // TODO: simulate it by in memory map
    }

    public void AppendAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        // TODO: simulate it by in memory map
    }

    public Task AppendAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = new CancellationToken())
    {
        // TODO: simulate it by in memory map
        return Task.CompletedTask;
    }

    public Task AppendAllBytesAsync(string path, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = new CancellationToken())
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

    public void AppendAllText(string path, ReadOnlySpan<char> contents)
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

    public FileAttributes GetAttributes(SafeFileHandle fileHandle)
    {
        return _executingFile.GetAttributes(fileHandle);
    }

    public DateTime GetCreationTime(string path)
    {
        return _executingFile.GetCreationTime(path);
    }

    public DateTime GetCreationTime(SafeFileHandle fileHandle)
    {
        return _executingFile.GetCreationTime(fileHandle);
    }

    public DateTime GetCreationTimeUtc(string path)
    {
        return _executingFile.GetCreationTimeUtc(path);
    }

    public DateTime GetCreationTimeUtc(SafeFileHandle fileHandle)
    {
        return _executingFile.GetCreationTimeUtc(fileHandle);
    }

    public DateTime GetLastAccessTime(string path)
    {
        return _executingFile.GetLastAccessTime(path);
    }

    public DateTime GetLastAccessTime(SafeFileHandle fileHandle)
    {
        return _executingFile.GetLastAccessTime(fileHandle);
    }

    public DateTime GetLastAccessTimeUtc(string path)
    {
        return _executingFile.GetLastAccessTimeUtc(path);
    }

    public DateTime GetLastAccessTimeUtc(SafeFileHandle fileHandle)
    {
        return _executingFile.GetLastAccessTimeUtc(fileHandle);
    }

    public DateTime GetLastWriteTime(string path)
    {
        return _executingFile.GetLastWriteTime(path);
    }

    public DateTime GetLastWriteTime(SafeFileHandle fileHandle)
    {
        return _executingFile.GetLastWriteTime(fileHandle);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        return _executingFile.GetLastWriteTimeUtc(path);
    }

    public DateTime GetLastWriteTimeUtc(SafeFileHandle fileHandle)
    {
        return _executingFile.GetLastWriteTimeUtc(fileHandle);
    }

    public UnixFileMode GetUnixFileMode(string path)
    {
        return _executingFile.GetUnixFileMode(path);
    }

    public UnixFileMode GetUnixFileMode(SafeFileHandle fileHandle)
    {
        return _executingFile.GetUnixFileMode(fileHandle);
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

    public void SetAttributes(SafeFileHandle fileHandle, FileAttributes fileAttributes)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTime(string path, DateTime creationTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTime(SafeFileHandle fileHandle, DateTime creationTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetCreationTimeUtc(SafeFileHandle fileHandle, DateTime creationTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastAccessTime(string path, DateTime lastAccessTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastAccessTime(SafeFileHandle fileHandle, DateTime lastAccessTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastAccessTimeUtc(SafeFileHandle fileHandle, DateTime lastAccessTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastWriteTime(string path, DateTime lastWriteTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastWriteTime(SafeFileHandle fileHandle, DateTime lastWriteTime)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetLastWriteTimeUtc(SafeFileHandle fileHandle, DateTime lastWriteTimeUtc)
    {
        // TODO: simulate it by in memory map
    }

    public void SetUnixFileMode(string path, UnixFileMode mode)
    {
        // TODO: simulate it by in memory map
    }

    public void SetUnixFileMode(SafeFileHandle fileHandle, UnixFileMode mode)
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

    public void WriteAllText(string path, ReadOnlySpan<char> contents)
    {
        // TODO: simulate it by in memory map
    }
}
