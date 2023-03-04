using System.IO.Abstractions;
using _42.Testing.System.IO.Abstractions.Parts;

namespace _42.Testing.System.IO.Abstractions;

public class DiagnosticFileSystem : IFileSystem
{
    public DiagnosticFileSystem(IFileSystem executingSystem, IFileSystemTracer processor)
    {
        Directory = new DiagnosticDirectory(executingSystem.Directory, this, processor);
        DirectoryInfo = new DiagnosticDirectoryInfoFactory(executingSystem.DirectoryInfo, this, processor);
        DriveInfo = new DiagnosticDriveInfoFactory(executingSystem.DriveInfo, this, processor);
        File = new DiagnosticFile(executingSystem.File, this, processor);
        FileInfo = new DiagnosticFileInfoFactory(executingSystem.FileInfo, this, processor);
        FileStream = new DiagnosticFileStreamFactory(executingSystem.FileStream, this, processor);
        FileSystemWatcher = new DiagnosticFileSystemWatcherFactory(executingSystem.FileSystemWatcher, this, processor);
        Path = new DiagnosticPath(executingSystem.Path, this, processor);
    }

    public IDirectory Directory { get; }

    public IDirectoryInfoFactory DirectoryInfo { get; }

    public IDriveInfoFactory DriveInfo { get; }

    public IFile File { get; }

    public IFileInfoFactory FileInfo { get; }

    public IFileStreamFactory FileStream { get; }

    public IFileSystemWatcherFactory FileSystemWatcher { get; }

    public IPath Path { get; }
}
