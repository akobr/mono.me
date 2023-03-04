using System;
using System.IO;
using System.IO.Abstractions;
using _42.Testing.System.IO.Abstractions.Wrappers;
using Microsoft.Win32.SafeHandles;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class ReadonlyFileStreamFactory : IFileStreamFactory
{
    public ReadonlyFileStreamFactory(IFileSystem fileSystem)
    {
        FileSystem = fileSystem;
    }

    public IFileSystem FileSystem { get; }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode)` instead.")]
    public Stream Create(string path, FileMode mode)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare, int)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare, int, FileOptions)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare, int, bool)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(SafeFileHandle, FileAccess)` instead.")]
    public Stream Create(SafeFileHandle handle, FileAccess access)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(SafeFileHandle, FileAccess, int)` instead.")]
    public Stream Create(SafeFileHandle handle, FileAccess access, int bufferSize)
    {
        return new MemoryStream();
    }

    [Obsolete("Use `IFileStreamFactory.New(SafeFileHandle, FileAccess, int, bool)` instead.")]
    public Stream Create(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync)
    {
        return new MemoryStream();
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access) instead. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access)
    {
        return new MemoryStream();
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access) instead, and optionally make a new SafeFileHandle with ownsHandle=false if needed. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access, bool ownsHandle)
    {
        return new MemoryStream();
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access, int bufferSize) instead, and optionally make a new SafeFileHandle with ownsHandle=false if needed. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize)
    {
        return new MemoryStream();
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) instead, and optionally make a new SafeFileHandle with ownsHandle=false if needed. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize, bool isAsync)
    {
        return new MemoryStream();
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access)
    {
        return new InMemoryStream();
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize)
    {
        return new InMemoryStream();
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync)
    {
        return new InMemoryStream();
    }

    public FileSystemStream New(string path, FileMode mode)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream New(string path, FileStreamOptions options)
    {
        return new InMemoryStream(path);
    }

    public FileSystemStream Wrap(FileStream fileStream)
    {
        return new FileSystemStreamWrapper(fileStream);
    }
}
