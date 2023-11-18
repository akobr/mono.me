using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticFile : IFile
{
    private readonly IFile _executingFile;
    private readonly IFileSystemTracer _processor;

    public DiagnosticFile(
        IFile executingFile,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFile = executingFile;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public Task AppendAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents });
        return _executingFile.AppendAllLinesAsync(path, contents, cancellationToken);
    }

    public Task AppendAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents, encoding });
        return _executingFile.AppendAllLinesAsync(path, contents, encoding, cancellationToken);
    }

    public Task AppendAllTextAsync(
        string path,
        string? contents,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents });
        return _executingFile.AppendAllTextAsync(path, contents, cancellationToken);
    }

    public Task AppendAllTextAsync(
        string path,
        string? contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents, encoding, cancellationToken });
        return _executingFile.AppendAllTextAsync(path, contents, encoding, cancellationToken);
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, cancellationToken });
        return _executingFile.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, cancellationToken });
        return _executingFile.ReadAllLinesAsync(path, cancellationToken);
    }

    public Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, encoding, cancellationToken });
        return _executingFile.ReadAllLinesAsync(path, encoding, cancellationToken);
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, cancellationToken });
        return _executingFile.ReadAllTextAsync(path, cancellationToken);
    }

    public Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, encoding, cancellationToken });
        return _executingFile.ReadAllTextAsync(path, encoding, cancellationToken);
    }

    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, cancellationToken });
        return _executingFile.ReadLinesAsync(path, cancellationToken);
    }

    public IAsyncEnumerable<string> ReadLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, encoding, cancellationToken });
        return _executingFile.ReadLinesAsync(path, encoding, cancellationToken);
    }

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, bytes, cancellationToken });
        return _executingFile.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    public Task WriteAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents, cancellationToken });
        return _executingFile.WriteAllLinesAsync(path, contents, cancellationToken);
    }

    public Task WriteAllLinesAsync(
        string path,
        IEnumerable<string> contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents, encoding, cancellationToken });
        return _executingFile.WriteAllLinesAsync(path, contents, encoding, cancellationToken);
    }

    public Task WriteAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents, cancellationToken });
        return _executingFile.WriteAllTextAsync(path, contents, cancellationToken);
    }

    public Task WriteAllTextAsync(
        string path,
        string? contents,
        Encoding encoding,
        CancellationToken cancellationToken = default)
    {
        _processor.Process(new object?[] { path, contents, encoding, cancellationToken });
        return _executingFile.WriteAllTextAsync(path, contents, encoding, cancellationToken);
    }

    public void AppendAllLines(string path, IEnumerable<string> contents)
    {
        _processor.Process(new object?[] { path, contents });
        _executingFile.AppendAllLines(path, contents);
    }

    public void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding)
    {
        _processor.Process(new object?[] { path, contents, encoding });
        _executingFile.AppendAllLines(path, contents, encoding);
    }

    public void AppendAllText(string path, string? contents)
    {
        _processor.Process(new object?[] { path, contents });
        _executingFile.AppendAllText(path, contents);
    }

    public void AppendAllText(string path, string? contents, Encoding encoding)
    {
        _processor.Process(new object?[] { path, contents });
        _executingFile.AppendAllText(path, contents, encoding);
    }

    public StreamWriter AppendText(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.AppendText(path);
    }

    public void Copy(string sourceFileName, string destFileName)
    {
        _processor.Process(new object?[] { sourceFileName, destFileName });
        _executingFile.Copy(sourceFileName, destFileName);
    }

    public void Copy(string sourceFileName, string destFileName, bool overwrite)
    {
        _processor.Process(new object?[] { sourceFileName, destFileName, overwrite });
        _executingFile.Copy(sourceFileName, destFileName, overwrite);
    }

    public FileSystemStream Create(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.Create(path);
    }

    public FileSystemStream Create(string path, int bufferSize)
    {
        _processor.Process(new object?[] { path, bufferSize });
        return _executingFile.Create(path, bufferSize);
    }

    public FileSystemStream Create(string path, int bufferSize, FileOptions options)
    {
        _processor.Process(new object?[] { path, bufferSize, options });
        return _executingFile.Create(path, bufferSize, options);
    }

    public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget)
    {
        _processor.Process(new object?[] { path, pathToTarget });
        return _executingFile.CreateSymbolicLink(path, pathToTarget);
    }

    public StreamWriter CreateText(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.CreateText(path);
    }

    [SupportedOSPlatform("windows")]
    public void Decrypt(string path)
    {
        _processor.Process(new object?[] { path });
        _executingFile.Decrypt(path);
    }

    public void Delete(string path)
    {
        _processor.Process(new object?[] { path });
        _executingFile.Delete(path);
    }

    [SupportedOSPlatform("windows")]
    public void Encrypt(string path)
    {
        _processor.Process(new object?[] { path });
        _executingFile.Encrypt(path);
    }

    public bool Exists(string? path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.Exists(path);
    }

    public FileAttributes GetAttributes(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetAttributes(path);
    }

    public FileAttributes GetAttributes(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetAttributes(fileHandle);
    }

    public DateTime GetCreationTime(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetCreationTime(path);
    }

    public DateTime GetCreationTime(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetCreationTime(fileHandle);
    }

    public DateTime GetCreationTimeUtc(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetCreationTimeUtc(path);
    }

    public DateTime GetCreationTimeUtc(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetCreationTimeUtc(fileHandle);
    }

    public DateTime GetLastAccessTime(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetLastAccessTime(path);
    }

    public DateTime GetLastAccessTime(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetLastAccessTime(fileHandle);
    }

    public DateTime GetLastAccessTimeUtc(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetLastAccessTimeUtc(path);
    }

    public DateTime GetLastAccessTimeUtc(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetLastAccessTimeUtc(fileHandle);
    }

    public DateTime GetLastWriteTime(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetLastWriteTime(path);
    }

    public DateTime GetLastWriteTime(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetLastWriteTime(fileHandle);
    }

    public DateTime GetLastWriteTimeUtc(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetLastWriteTimeUtc(path);
    }

    public DateTime GetLastWriteTimeUtc(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetLastWriteTimeUtc(fileHandle);
    }

    public UnixFileMode GetUnixFileMode(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.GetUnixFileMode(path);
    }

    public UnixFileMode GetUnixFileMode(SafeFileHandle fileHandle)
    {
        _processor.Process(new object?[] { fileHandle });
        return _executingFile.GetUnixFileMode(fileHandle);
    }

    public void Move(string sourceFileName, string destFileName)
    {
        _processor.Process(new object?[] { sourceFileName, destFileName });
        _executingFile.Move(sourceFileName, destFileName);
    }

    public void Move(string sourceFileName, string destFileName, bool overwrite)
    {
        _processor.Process(new object?[] { sourceFileName, destFileName, overwrite });
        _executingFile.Move(sourceFileName, destFileName, overwrite);
    }

    public FileSystemStream Open(string path, FileMode mode)
    {
        _processor.Process(new object?[] { path, mode });
        return _executingFile.Open(path, mode);
    }

    public FileSystemStream Open(string path, FileMode mode, FileAccess access)
    {
        _processor.Process(new object?[] { path, mode, access });
        return _executingFile.Open(path, mode, access);
    }

    public FileSystemStream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        _processor.Process(new object?[] { path, mode, access, share });
        return _executingFile.Open(path, mode, access, share);
    }

    public FileSystemStream Open(string path, FileStreamOptions options)
    {
        _processor.Process(new object?[] { path, options });
        return _executingFile.Open(path, options);
    }

    public FileSystemStream OpenRead(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.OpenRead(path);
    }

    public StreamReader OpenText(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.OpenText(path);
    }

    public FileSystemStream OpenWrite(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.OpenWrite(path);
    }

    public byte[] ReadAllBytes(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.ReadAllBytes(path);
    }

    public string[] ReadAllLines(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.ReadAllLines(path);
    }

    public string[] ReadAllLines(string path, Encoding encoding)
    {
        _processor.Process(new object?[] { path, encoding });
        return _executingFile.ReadAllLines(path, encoding);
    }

    public string ReadAllText(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.ReadAllText(path);
    }

    public string ReadAllText(string path, Encoding encoding)
    {
        _processor.Process(new object?[] { path, encoding });
        return _executingFile.ReadAllText(path, encoding);
    }

    public IEnumerable<string> ReadLines(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFile.ReadLines(path);
    }

    public IEnumerable<string> ReadLines(string path, Encoding encoding)
    {
        _processor.Process(new object?[] { path, encoding });
        return _executingFile.ReadLines(path, encoding);
    }

    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        _processor.Process(new object?[] { sourceFileName, destinationFileName, destinationBackupFileName });
        _executingFile.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    public void Replace(
        string sourceFileName,
        string destinationFileName,
        string? destinationBackupFileName,
        bool ignoreMetadataErrors)
    {
        _processor.Process(new object?[] { sourceFileName, destinationFileName, destinationBackupFileName, ignoreMetadataErrors });
        _executingFile.Replace(sourceFileName, destinationFileName, destinationBackupFileName, ignoreMetadataErrors);
    }

    public IFileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget)
    {
        _processor.Process(new object?[] { linkPath, returnFinalTarget });
        return _executingFile.ResolveLinkTarget(linkPath, returnFinalTarget);
    }

    public void SetAttributes(string path, FileAttributes fileAttributes)
    {
        _processor.Process(new object?[] { path, fileAttributes });
        _executingFile.SetAttributes(path, fileAttributes);
    }

    public void SetAttributes(SafeFileHandle fileHandle, FileAttributes fileAttributes)
    {
        _processor.Process(new object?[] { fileHandle, fileAttributes });
        _executingFile.SetAttributes(fileHandle, fileAttributes);
    }

    public void SetCreationTime(string path, DateTime creationTime)
    {
        _processor.Process(new object?[] { path, creationTime });
        _executingFile.SetCreationTime(path, creationTime);
    }

    public void SetCreationTime(SafeFileHandle fileHandle, DateTime creationTime)
    {
        _processor.Process(new object?[] { fileHandle, creationTime });
        _executingFile.SetCreationTime(fileHandle, creationTime);
    }

    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc)
    {
        _processor.Process(new object?[] { path, creationTimeUtc });
        _executingFile.SetCreationTimeUtc(path, creationTimeUtc);
    }

    public void SetCreationTimeUtc(SafeFileHandle fileHandle, DateTime creationTimeUtc)
    {
        _processor.Process(new object?[] { fileHandle, creationTimeUtc });
        _executingFile.SetCreationTimeUtc(fileHandle, creationTimeUtc);
    }

    public void SetLastAccessTime(string path, DateTime lastAccessTime)
    {
        _processor.Process(new object?[] { path, lastAccessTime });
        _executingFile.SetLastAccessTime(path, lastAccessTime);
    }

    public void SetLastAccessTime(SafeFileHandle fileHandle, DateTime lastAccessTime)
    {
        _processor.Process(new object?[] { fileHandle, lastAccessTime });
        _executingFile.SetLastAccessTime(fileHandle, lastAccessTime);
    }

    public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc)
    {
        _processor.Process(new object?[] { path, lastAccessTimeUtc });
        _executingFile.SetLastAccessTimeUtc(path, lastAccessTimeUtc);
    }

    public void SetLastAccessTimeUtc(SafeFileHandle fileHandle, DateTime lastAccessTimeUtc)
    {
        _processor.Process(new object?[] { fileHandle, lastAccessTimeUtc });
        _executingFile.SetLastAccessTimeUtc(fileHandle, lastAccessTimeUtc);
    }

    public void SetLastWriteTime(string path, DateTime lastWriteTime)
    {
        _processor.Process(new object?[] { path, lastWriteTime });
        _executingFile.SetLastWriteTime(path, lastWriteTime);
    }

    public void SetLastWriteTime(SafeFileHandle fileHandle, DateTime lastWriteTime)
    {
        _processor.Process(new object?[] { fileHandle, lastWriteTime });
        _executingFile.SetLastWriteTime(fileHandle, lastWriteTime);
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        _processor.Process(new object?[] { path, lastWriteTimeUtc });
        _executingFile.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
    }

    public void SetLastWriteTimeUtc(SafeFileHandle fileHandle, DateTime lastWriteTimeUtc)
    {
        _processor.Process(new object?[] { fileHandle, lastWriteTimeUtc });
        _executingFile.SetLastWriteTimeUtc(fileHandle, lastWriteTimeUtc);
    }

    public void SetUnixFileMode(string path, UnixFileMode mode)
    {
        _processor.Process(new object?[] { path, mode });
        _executingFile.SetUnixFileMode(path, mode);
    }

    public void SetUnixFileMode(SafeFileHandle fileHandle, UnixFileMode mode)
    {
        _processor.Process(new object?[] { fileHandle, mode });
        _executingFile.SetUnixFileMode(fileHandle, mode);
    }

    public void WriteAllBytes(string path, byte[] bytes)
    {
        _processor.Process(new object?[] { path, bytes });
        _executingFile.WriteAllBytes(path, bytes);
    }

    public void WriteAllLines(string path, string[] contents)
    {
        _processor.Process(new object?[] { path, contents });
        _executingFile.WriteAllLines(path, contents);
    }

    public void WriteAllLines(string path, IEnumerable<string> contents)
    {
        _processor.Process(new object?[] { path, contents });
        _executingFile.WriteAllLines(path, contents);
    }

    public void WriteAllLines(string path, string[] contents, Encoding encoding)
    {
        _processor.Process(new object?[] { path, contents, encoding });
        _executingFile.WriteAllLines(path, contents, encoding);
    }

    public void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding)
    {
        _processor.Process(new object?[] { path, contents, encoding });
        _executingFile.WriteAllLines(path, contents, encoding);
    }

    public void WriteAllText(string path, string? contents)
    {
        _processor.Process(new object?[] { path, contents });
        _executingFile.WriteAllText(path, contents);
    }

    public void WriteAllText(string path, string? contents, Encoding encoding)
    {
        _processor.Process(new object?[] { path, contents, encoding });
        _executingFile.WriteAllText(path, contents, encoding);
    }
}
