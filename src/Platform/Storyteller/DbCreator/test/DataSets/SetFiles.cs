using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// Folders of sets in a MockFileSystem, with paths of the platform the tests run on.
internal static class SetFiles
{
    public static readonly string Root = MockUnixSupport.Path(@"C:\sets");

    public static string Folder(params string[] segments)
    {
        return Path.Combine([Root, .. segments]);
    }

    // The files below the folder, by their paths with '/' separators.
    public static MockFileSystem Create(string folder, params (string Path, string Content)[] files)
    {
        var fileSystem = new MockFileSystem();
        Add(fileSystem, folder, files);
        return fileSystem;
    }

    public static void Add(MockFileSystem fileSystem, string folder, params (string Path, string Content)[] files)
    {
        fileSystem.AddDirectory(folder);

        foreach (var (path, content) in files)
        {
            fileSystem.AddFile(
                Path.Combine([folder, .. path.Split('/')]),
                new MockFileData(Encoding.UTF8.GetBytes(content)));
        }
    }
}
