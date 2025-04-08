using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticFileVersionInfoFactory : IFileVersionInfoFactory
{
    private readonly IFileVersionInfoFactory _executingFactory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticFileVersionInfoFactory(
        IFileVersionInfoFactory executingFactory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFactory = executingFactory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public IFileVersionInfo GetVersionInfo(string fileName)
    {
        _processor.Process(new object?[] { fileName });
        return _executingFactory.GetVersionInfo(fileName);
    }
}
