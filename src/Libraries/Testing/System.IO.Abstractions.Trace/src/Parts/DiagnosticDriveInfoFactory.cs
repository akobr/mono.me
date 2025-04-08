using System;
using System.IO;
using System.IO.Abstractions;

namespace _42.Testing.System.IO.Abstractions.Parts;

public class DiagnosticDriveInfoFactory : IDriveInfoFactory
{
    private readonly IDriveInfoFactory _executingFactory;
    private readonly IFileSystemTracer _processor;

    public DiagnosticDriveInfoFactory(
        IDriveInfoFactory executingFactory,
        IFileSystem diagnosticSystem,
        IFileSystemTracer processor)
    {
        _executingFactory = executingFactory;
        _processor = processor;
        FileSystem = diagnosticSystem;
    }

    public IFileSystem FileSystem { get; }

    public IDriveInfo[] GetDrives()
    {
        _processor.Process();
        return _executingFactory.GetDrives();
    }

    public IDriveInfo New(string driveName)
    {
        _processor.Process(new object?[] { driveName });
        return _executingFactory.New(driveName);
    }

    public IDriveInfo? Wrap(DriveInfo? driveInfo)
    {
        _processor.Process(new object?[] { driveInfo });
        return _executingFactory.Wrap(driveInfo);
    }
}
