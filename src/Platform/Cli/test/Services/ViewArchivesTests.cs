using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Cli.Services;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services;

public class ViewArchivesTests
{
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));

    [Theory]
    [InlineData("annotations/rst.a.json", false)]
    [InlineData("rst.a.json", false)]
    [InlineData("deep/nested/folders/exe.template.json", false)]
    [InlineData("./rst.a.json", false)]
    [InlineData(".git/rst.a.json", true)]
    [InlineData(".github/workflows/x.json", true)]
    [InlineData("repo/.vscode/settings.json", true)]
    [InlineData("annotations/.rst.a.json", true)]
    [InlineData("schemas/._exe.schema.json", true)]
    [InlineData(".DS_Store", true)]
    [InlineData("__MACOSX/rst.a.json", true)]
    [InlineData("__macosx/rst.a.json", true)]
    [InlineData("export/__MACOSX/rst.a.json", false)]
    [InlineData("", true)]
    public void SkippedPaths_AreTheServersRule(string path, bool isSkipped)
    {
        ViewArchives.IsSkippedPath(path).ShouldBe(isSkipped);
    }

    [Fact]
    public void PackFolder_TakesEveryJsonFileBelowIt_WithoutHiddenFoldersAndFiles()
    {
        AddFile(@"C:\work\repo\annotations\rst.a.json");
        AddFile(@"C:\work\repo\annotations\usg.a.b.JSON");
        AddFile(@"C:\work\repo\configurations\rst.a.conf.json");
        AddFile(@"C:\work\repo\configurations\rst.a.conf.1.json");
        AddFile(@"C:\work\repo\export.json");
        AddFile(@"C:\work\repo\flat\exe.template.json");
        AddFile(@"C:\work\repo\README.md");
        AddFile(@"C:\work\repo\exe.template.jsonc");
        AddFile(@"C:\work\repo\.git\HEAD");
        AddFile(@"C:\work\repo\.git\objects\info.json");
        AddFile(@"C:\work\repo\.github\workflows\sync.json");
        AddFile(@"C:\work\repo\schemas\.hidden.schema.json");
        AddFile(@"C:\work\repo\schemas\._exe.schema.json");
        AddFile(@"C:\work\repo\schemas\.cache\exe.schema.json");
        AddFile(@"C:\work\repo\__MACOSX\annotations\rst.a.json");

        var (archive, count) = ViewArchives.PackFolder(_fileSystem, "repo");

        using (archive)
        {
            count.ShouldBe(6);
            Entries(archive).ShouldBe(
            [
                "annotations/rst.a.json",
                "annotations/usg.a.b.JSON",
                "configurations/rst.a.conf.1.json",
                "configurations/rst.a.conf.json",
                "export.json",
                "flat/exe.template.json",
            ]);
        }
    }

    [Fact]
    public void PackFolder_KeepsTheContentOfTheFiles()
    {
        AddFile(@"C:\work\view\rst.a.conf.json", "{\"a\":1}\n");

        var (archive, _) = ViewArchives.PackFolder(_fileSystem, P(@"C:\work\view"));

        using (archive)
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("rst.a.conf.json")!.Open(), Encoding.UTF8))
        {
            reader.ReadToEnd().ShouldBe("{\"a\":1}\n");
        }
    }

    [Fact]
    public void PackFolder_OfOnlyHiddenFiles_PacksNothing()
    {
        AddFile(@"C:\work\repo\.git\config.json");
        AddFile(@"C:\work\repo\.hidden.json");

        var (archive, count) = ViewArchives.PackFolder(_fileSystem, "repo");

        using (archive)
        {
            count.ShouldBe(0);
            Entries(archive).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Extract_RefusesEntriesOutsideTheFolder()
    {
        using var archive = new MemoryStream(Zip(("../evil.json", "{}")));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => ViewArchives.ExtractAsync(_fileSystem, archive, P(@"C:\work\out")));

        exception.Message.ShouldBe("The archive entry '../evil.json' points outside the folder.");
        _fileSystem.File.Exists(P(@"C:\work\evil.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Extract_WritesTheFoldersOfTheArchive()
    {
        using var archive = new MemoryStream(Zip(("annotations/rst.a.json", "{}"), ("templates/exe.template.json", "{\"t\":1}")));

        var count = await ViewArchives.ExtractAsync(_fileSystem, archive, P(@"C:\work\out"));

        count.ShouldBe(2);
        _fileSystem.File.ReadAllText(P(@"C:\work\out\templates\exe.template.json")).ShouldBe("{\"t\":1}");
    }

    // The mock file system takes Windows paths; on Unix they become /work, …
    private static string P(string path) => MockUnixSupport.Path(path);

    private static List<string> Entries(Stream archive)
    {
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        return zip.Entries.Select(entry => entry.FullName).ToList();
    }

    private static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private void AddFile(string path, string content = "{}")
    {
        _fileSystem.AddFile(P(path), new MockFileData(content));
    }
}
