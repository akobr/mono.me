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
