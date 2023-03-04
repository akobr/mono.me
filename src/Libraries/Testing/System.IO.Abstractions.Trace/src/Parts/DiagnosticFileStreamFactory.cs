using System;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticFileStreamFactory : IFileStreamFactory
{
    private readonly IFileStreamFactory _executingFactory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticFileStreamFactory(
        IFileStreamFactory executingFactory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFactory = executingFactory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode)` instead.")]
    public Stream Create(string path, FileMode mode)
    {
        _processor.Process(new object?[] { path, mode });
        return _executingFactory.Create(path, mode);
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access)
    {
        _processor.Process(new object?[] { path, mode, access });
        return _executingFactory.Create(path, mode, access);
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share)
    {
        _processor.Process(new object?[] { path, mode, access, share });
        return _executingFactory.Create(path, mode, access, share);
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare, int)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
    {
        _processor.Process(new object?[] { path, mode, access, share, bufferSize });
        return _executingFactory.Create(path, mode, access, share, bufferSize);
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare, int, FileOptions)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        _processor.Process(new object?[] { path, mode, access, share, bufferSize, options });
        return _executingFactory.Create(path, mode, access, share, bufferSize, options);
    }

    [Obsolete("Use `IFileStreamFactory.New(string, FileMode, FileAccess, FileShare, int, bool)` instead.")]
    public Stream Create(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
    {
        _processor.Process(new object?[] { path, mode, access, share, bufferSize, useAsync });
        return _executingFactory.Create(path, mode, access, share, bufferSize, useAsync);
    }

    [Obsolete("Use `IFileStreamFactory.New(SafeFileHandle, FileAccess)` instead.")]
    public Stream Create(SafeFileHandle handle, FileAccess access)
    {
        _processor.Process(new object?[] { handle, access });
        return _executingFactory.Create(handle, access);
    }

    [Obsolete("Use `IFileStreamFactory.New(SafeFileHandle, FileAccess, int)` instead.")]
    public Stream Create(SafeFileHandle handle, FileAccess access, int bufferSize)
    {
        _processor.Process(new object?[] { handle, access, bufferSize });
        return _executingFactory.Create(handle, access, bufferSize);
    }

    [Obsolete("Use `IFileStreamFactory.New(SafeFileHandle, FileAccess, int, bool)` instead.")]
    public Stream Create(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync)
    {
        _processor.Process(new object?[] { handle, access, bufferSize, isAsync });
        return _executingFactory.Create(handle, access, bufferSize, isAsync);
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access) instead. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access)
    {
        _processor.Process(new object?[] { handle, access });
        return _executingFactory.Create(handle, access);
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access) instead, and optionally make a new SafeFileHandle with ownsHandle=false if needed. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access, bool ownsHandle)
    {
        _processor.Process(new object?[] { handle, access, ownsHandle });
        return _executingFactory.Create(handle, access, ownsHandle);
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access, int bufferSize) instead, and optionally make a new SafeFileHandle with ownsHandle=false if needed. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize)
    {
        _processor.Process(new object?[] { handle, access, ownsHandle, bufferSize });
        return _executingFactory.Create(handle, access, ownsHandle, bufferSize);
    }

    [Obsolete("This method has been deprecated. Please use new Create(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) instead, and optionally make a new SafeFileHandle with ownsHandle=false if needed. http://go.microsoft.com/fwlink/?linkid=14202")]
    public Stream Create(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize, bool isAsync)
    {
        _processor.Process(new object?[] { handle, access, ownsHandle, bufferSize, isAsync });
        return _executingFactory.Create(handle, access, ownsHandle, bufferSize, isAsync);
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access)
    {
        _processor.Process(new object?[] { handle, access });
        return _executingFactory.New(handle, access);
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize)
    {
        _processor.Process(new object?[] { handle, access, bufferSize });
        return _executingFactory.New(handle, access, bufferSize);
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync)
    {
        _processor.Process(new object?[] { handle, access, bufferSize, isAsync });
        return _executingFactory.New(handle, access, bufferSize, isAsync);
    }

    public FileSystemStream New(string path, FileMode mode)
    {
        _processor.Process(new object?[] { path, mode });
        return _executingFactory.New(path, mode);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access)
    {
        _processor.Process(new object?[] { path, mode, access });
        return _executingFactory.New(path, mode, access);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share)
    {
        _processor.Process(new object?[] { path, mode, access, share });
        return _executingFactory.New(path, mode, access, share);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize)
    {
        _processor.Process(new object?[] { path, mode, access, share, bufferSize });
        return _executingFactory.New(path, mode, access, share, bufferSize);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync)
    {
        _processor.Process(new object?[] { path, mode, access, share, bufferSize, useAsync });
        return _executingFactory.New(path, mode, access, share, bufferSize, useAsync);
    }

    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        _processor.Process(new object?[] { path, mode, access, share, bufferSize, options });
        return _executingFactory.New(path, mode, access, share, bufferSize, options);
    }

    public FileSystemStream New(string path, FileStreamOptions options)
    {
        _processor.Process(new object?[] { path, options });
        return _executingFactory.New(path, options);
    }

    public FileSystemStream Wrap(FileStream fileStream)
    {
        _processor.Process(new object?[] { fileStream });
        return _executingFactory.Wrap(fileStream);
    }
}
