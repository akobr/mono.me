using System;
using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticFileSystemWatcherFactory : IFileSystemWatcherFactory
{
    private readonly IFileSystemWatcherFactory _executingFactory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticFileSystemWatcherFactory(
        IFileSystemWatcherFactory executingFactory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFactory = executingFactory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    [Obsolete("Use `IFileSystemWatcherFactory.New()` instead")]
    public IFileSystemWatcher CreateNew()
    {
        _processor.Process();
        return _executingFactory.CreateNew();
    }

    [Obsolete("Use `IFileSystemWatcherFactory.New(string)` instead")]
    public IFileSystemWatcher CreateNew(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFactory.CreateNew(path);
    }

    [Obsolete("Use `IFileSystemWatcherFactory.New(string, string)` instead")]
    public IFileSystemWatcher CreateNew(string path, string filter)
    {
        _processor.Process(new object?[] { path, filter });
        return _executingFactory.CreateNew(path, filter);
    }

    public IFileSystemWatcher New()
    {
        _processor.Process();
        return _executingFactory.New();
    }

    public IFileSystemWatcher New(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFactory.New(path);
    }

    public IFileSystemWatcher New(string path, string filter)
    {
        _processor.Process(new object?[] { path, filter });
        return _executingFactory.New(path, filter);
    }

    public IFileSystemWatcher? Wrap(FileSystemWatcher? fileSystemWatcher)
    {
        _processor.Process(new object?[] { fileSystemWatcher });
        return _executingFactory.Wrap(fileSystemWatcher);
    }
}
