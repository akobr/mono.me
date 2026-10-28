using System.IO.Abstractions.TestingHelpers;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// The call the cache makes on Linux and macOS, checked on every OS.
public class UnixModeMockFileSystemTests
{
    [Fact]
    public void CreateDirectory_WithAUnixMode_CreatesTheDirectory()
    {
        var fileSystem = new UnixModeMockFileSystem();
        var path = MockUnixSupport.Path(@"C:\cache\storyteller");

#pragma warning disable CA1416 // The mock implements the overload on every OS.
        fileSystem.Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416

        fileSystem.Directory.Exists(path).ShouldBeTrue();
        fileSystem.DirectoryInfo.New(path).Exists.ShouldBeTrue();
    }

    [Fact]
    public void TheDirectory_SharesTheFilesOfTheFileSystem()
    {
        var fileSystem = new UnixModeMockFileSystem();
        var path = MockUnixSupport.Path(@"C:\cache\storyteller\entry.cache");

        fileSystem.AddFile(path, new MockFileData("x"));

        fileSystem.Directory.GetFiles(MockUnixSupport.Path(@"C:\cache\storyteller")).ShouldBe([path]);
    }
}
