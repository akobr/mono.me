using System.IO.Abstractions;
using _42.Testing.System.IO.Abstractions.Parts;

namespace _42.Testing.System.IO.Abstractions;

public class ReadonlyFileSystem : IFileSystem
{
    public ReadonlyFileSystem(IFileSystem executingSystem)
    {
        Directory = new ReadonlyDirectory(executingSystem.Directory, this);
        DirectoryInfo = executingSystem.DirectoryInfo;
        DriveInfo = executingSystem.DriveInfo;
        File = new ReadonlyFile(executingSystem.File, this);
        FileInfo = executingSystem.FileInfo;
        FileStream = new ReadonlyFileStreamFactory(this);
        FileSystemWatcher = executingSystem.FileSystemWatcher;
        Path = executingSystem.Path;
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
