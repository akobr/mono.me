using System;
using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticDirectoryInfoFactory : IDirectoryInfoFactory
{
    private readonly IDirectoryInfoFactory _executingFactory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticDirectoryInfoFactory(
        IDirectoryInfoFactory executingFactory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFactory = executingFactory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    [Obsolete("Use `IDirectoryInfoFactory.New(string)` instead")]
    public IDirectoryInfo FromDirectoryName(string directoryName)
    {
        _processor.Process(new object?[] { directoryName });
        return _executingFactory.FromDirectoryName(directoryName);
    }

    public IDirectoryInfo New(string path)
    {
        _processor.Process(new object?[] { path });
        return _executingFactory.New(path);
    }

    public IDirectoryInfo? Wrap(DirectoryInfo? directoryInfo)
    {
        _processor.Process(new object?[] { directoryInfo });
        return _executingFactory.Wrap(directoryInfo);
    }
}
