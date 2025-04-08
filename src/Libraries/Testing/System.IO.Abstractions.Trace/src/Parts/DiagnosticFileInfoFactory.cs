using System;
using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticFileInfoFactory : IFileInfoFactory
{
    private readonly IFileInfoFactory _executingFactory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticFileInfoFactory(
        IFileInfoFactory executingFactory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFactory = executingFactory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public IFileInfo New(string fileName)
    {
        _processor.Process(new object?[] { fileName });
        return _executingFactory.New(fileName);
    }

    public IFileInfo? Wrap(FileInfo? fileInfo)
    {
        _processor.Process(new object?[] { fileInfo });
        return _executingFactory.Wrap(fileInfo);
    }
}
