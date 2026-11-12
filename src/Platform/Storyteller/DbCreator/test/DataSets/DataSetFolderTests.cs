using System.IO.Abstractions.TestingHelpers;
using System.Text;
using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetFolderTests
{
    private static readonly string Folder = SetFiles.Folder("showcase");

    [Fact]
    public void GetFiles_ListsEveryFileInOrdinalOrderWithoutTheSkippedPaths()
    {
        var fileSystem = SetFiles.Create(
            Folder,
            ("set.json", "{}"),
            ("annotations/sbt.b.json", "{}"),
            ("annotations/sbt.a.json", "{}"),
            ("annotations/._sbt.a.json", "{}"),
            ("annotations.jsonl", string.Empty),
            ("Zebra.md", string.Empty),
            (".hidden.json", "{}"),
            (".git/config", string.Empty),
            ("__MACOSX/annotations/sbt.a.json", "{}"),
            ("configurations/.cache/sbt.a.conf.json", "{}"),
            ("configurations/sbt.a.conf.json", "{}"));

        var files = DataSetFolder.GetFiles(fileSystem, Folder);

        files.Select(file => file.Path).ShouldBe(
        [
            "Zebra.md",
            "annotations.jsonl",
            "annotations/sbt.a.json",
            "annotations/sbt.b.json",
            "configurations/sbt.a.conf.json",
            "set.json",
        ]);
        files[2].FullPath.ShouldBe(Path.Combine(Folder, "annotations", "sbt.a.json"));
    }

    [Fact]
    public void GetEntries_LeavesTheManifestAndTheLinesAtTheRootOut()
    {
        var fileSystem = SetFiles.Create(
            Folder,
            ("set.json", "{}"),
            ("annotations.jsonl", "{}"),
            ("nested/set.json", "{}"),
            ("nested/annotations.jsonl", "{}"),
            ("annotations/sbt.a.json", """{ "Title": "A" }"""));

        var entries = DataSetFolder.GetEntries(fileSystem, Folder);

        entries.Select(entry => entry.Path).ShouldBe(["annotations/sbt.a.json", "nested/annotations.jsonl", "nested/set.json"]);
        entries[0].Length.ShouldBe(16);
        using var reader = new StreamReader(entries[0].Open());
        reader.ReadToEnd().ShouldBe("""{ "Title": "A" }""");
    }

    [Fact]
    public void ReadDocuments_ReadsTheFoldersOfAnExport()
    {
        var fileSystem = SetFiles.Create(
            Folder,
            ("set.json", """{ "Organization": "fabrikam" }"""),
            ("annotations.jsonl", """{"AnnotationKey":"sbt.b"}"""),
            ("export.json", "{}"),
            ("README.md", "# Showcase"),
            ("annotations/sbt.a.json", """{ "Values": { "amount": 1.10 } }"""),
            ("annotations/sbt.broken.json", "{ not json"),
            ("configurations/sbt.a.conf.json", """{ "Enabled": true }"""),
            ("configurations/sbt.a.conf.1.json", """{ "Enabled": false }"""),
            ("schemas/sbt.schema.json", """{ "type": "object" }"""),
            ("templates/sbt.template.json", """{ "Enabled": false }"""),
            ("flat/sbt.c.json", "{}"));

        var documents = DataSetFolder.ReadDocuments(fileSystem, Folder);

        documents.Files.Select(file => (file.Path, file.Kind)).ShouldBe(
        [
            ("annotations/sbt.a.json", ViewDocumentKind.Annotation),
            ("annotations/sbt.broken.json", ViewDocumentKind.Annotation),
            ("configurations/sbt.a.conf.json", ViewDocumentKind.Configuration),
            ("flat/sbt.c.json", ViewDocumentKind.Annotation),
            ("schemas/sbt.schema.json", ViewDocumentKind.TypeSchema),
            ("templates/sbt.template.json", ViewDocumentKind.Template),
        ]);

        // the version file, export.json and README.md; never the manifest or annotations.jsonl
        documents.IgnoredFiles.ShouldBe(3);
        documents.Files[0].Content!["Values"]!["amount"]!.ToString().ShouldBe("1.10");
        documents.Files[1].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
    }

    [Fact]
    public void ComputeHash_IsTheSameForCrlfAndLfLineEnds()
    {
        var lf = SetFiles.Create(Folder, ("set.json", "{\n  \"Name\": \"showcase\"\n}\n"), ("annotations/sbt.a.json", "{\n}\n"));
        var crlf = SetFiles.Create(Folder, ("set.json", "{\r\n  \"Name\": \"showcase\"\r\n}\r\n"), ("annotations/sbt.a.json", "{\r\n}\r\n"));

        var hash = DataSetFolder.ComputeHash(lf, Folder);

        hash.ShouldMatch("^[0-9a-f]{64}$");
        DataSetFolder.ComputeHash(crlf, Folder).ShouldBe(hash);
    }

    [Fact]
    public void ComputeHash_ChangesWithAnyFileButNotWithASkippedOne()
    {
        (string Path, string Content)[] files = [("set.json", "{}"), ("annotations/sbt.a.json", "{}"), ("annotations.jsonl", "{\"AnnotationKey\":\"sbt.b\"}")];
        var hash = DataSetFolder.ComputeHash(SetFiles.Create(Folder, files), Folder);

        DataSetFolder.ComputeHash(SetFiles.Create(Folder, [.. files, (".git/HEAD", "ref"), ("annotations/.DS_Store", "x")]), Folder).ShouldBe(hash);

        DataSetFolder.ComputeHash(SetFiles.Create(Folder, [.. files, ("README.md", string.Empty)]), Folder).ShouldNotBe(hash);
        DataSetFolder.ComputeHash(SetFiles.Create(Folder, files[..2]), Folder).ShouldNotBe(hash);
        DataSetFolder.ComputeHash(SetFiles.Create(Folder, [files[0], ("annotations/sbt.c.json", "{}"), files[2]]), Folder).ShouldNotBe(hash);
        DataSetFolder.ComputeHash(SetFiles.Create(Folder, [files[0], ("annotations/sbt.a.json", "{ }"), files[2]]), Folder).ShouldNotBe(hash);
    }

    [Fact]
    public void NormalizeLineEnds_ChangesOnlyTheCrlfOfATextFile()
    {
        DataSetFolder.NormalizeLineEnds(Encoding.UTF8.GetBytes("a\r\nb\rc\n\r\n")).ShouldBe(Encoding.UTF8.GetBytes("a\nb\rc\n\n"));

        // a NUL byte makes it binary, as for Git
        byte[] binary = [1, 0, 13, 10, 2];
        DataSetFolder.NormalizeLineEnds(binary).ShouldBe(binary);

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(Folder, "image.bin"), new MockFileData([0, 13, 10]));
        var withCrlf = DataSetFolder.ComputeHash(fileSystem, Folder);
        fileSystem.AddFile(Path.Combine(Folder, "image.bin"), new MockFileData([0, 10]));
        DataSetFolder.ComputeHash(fileSystem, Folder).ShouldNotBe(withCrlf);
    }
}
