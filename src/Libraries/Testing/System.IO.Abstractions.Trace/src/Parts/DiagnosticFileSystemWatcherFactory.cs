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
