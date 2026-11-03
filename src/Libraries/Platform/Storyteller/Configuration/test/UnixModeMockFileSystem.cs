using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// A MockFileSystem that creates a directory with a Unix mode. The cache creates its directory with one on Linux and
// macOS, and TestableIO's MockDirectory throws NotImplementedException for that overload; the mode is not kept, which
// the [UnixFact] tests check on the real file system.
internal sealed class UnixModeMockFileSystem : MockFileSystem
{
    private IDirectory? _directory;

    public override IDirectory Directory => _directory ??= new UnixModeMockDirectory(this, base.Directory.GetCurrentDirectory());

    private sealed class UnixModeMockDirectory : MockDirectory
    {
        public UnixModeMockDirectory(IMockFileDataAccessor mockFileDataAccessor, string currentDirectory)
            : base(mockFileDataAccessor, currentDirectory)
        {
        }

        public override IDirectoryInfo CreateDirectory(string path, UnixFileMode unixCreateMode)
        {
            return CreateDirectory(path);
        }
    }
}
