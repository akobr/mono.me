using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ConfigurationArchiveReaderTests
{
    private static readonly ConfigurationTransferOptions Options = new();

    [Fact]
    public void Read_FindsConfigurationFilesInSubfolders()
    {
        using var zip = Zip(
            ("configs/", string.Empty),
            ("configs/rst.payments.json", """{"a":1}"""),
            ("configs/subjects/sbt.shop.json", """{"b":2}"""),
            ("usg.shop.payments.json", """{"c":3}"""));

        var content = ConfigurationArchiveReader.Read(zip, Options);

        content.Files.Select(file => file.Key.ToString()).ShouldBe(["rst.payments", "sbt.shop", "usg.shop.payments"]);
        content.Files.Select(file => file.Path).ShouldBe(["configs/rst.payments.json", "configs/subjects/sbt.shop.json", "usg.shop.payments.json"]);
        content.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.IgnoredFiles.ShouldBe(0);
    }

    [Fact]
    public void Read_IgnoresVersionFilesTheManifestAndOtherFiles()
    {
        using var zip = Zip(
            ("rst.payments.json", """{"a":1}"""),
            ("rst.payments.1.json", """{"a":0}"""),
            ("export.json", "{}"),
            ("README.md", "# configurations"),
            ("notes.json", "{}"));

        var content = ConfigurationArchiveReader.Read(zip, Options);

        content.Files.Count.ShouldBe(1);
        content.IgnoredFiles.ShouldBe(4);
    }

    [Fact]
    public void Read_SkipsGitAndMacFolders()
    {
        using var zip = Zip(
            (".git/HEAD", "ref: refs/heads/main"),
            (".git/rst.inside.json", "{}"),
            ("repo/.git/config", "[core]"),
            ("__MACOSX/._rst.payments.json", "binary"),
            ("._rst.payments.json", "binary"),
            ("rst.payments.json", """{"a":1}"""));

        var content = ConfigurationArchiveReader.Read(zip, Options);

        content.Files.Select(file => file.Path).ShouldBe(["rst.payments.json"]);
        content.IgnoredFiles.ShouldBe(0);
    }

    [Fact]
    public void Read_ReportsInvalidDocuments()
    {
        using var zip = Zip(
            ("rst.array.json", "[1,2]"),
            ("rst.broken.json", """{"a":"""),
            ("rst.duplicate.json", """{"a":1,"a":2}"""),
            ("rst.trailing.json", """{"a":1} {"b":2}"""),
            ("rst.commented.json", "// a comment\n{\"a\":1 /* inline */}\n// end"));

        var files = ConfigurationArchiveReader.Read(zip, Options).Files.ToDictionary(file => file.Key.ToString());

        files["rst.array"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["rst.broken"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["rst.duplicate"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["rst.trailing"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["rst.commented"].ErrorCode.ShouldBeNull();
        files["rst.commented"].Content!["a"]!.Value<int>().ShouldBe(1);
    }

    [Fact]
    public void Read_KeepsTheFirstFileOfAKey()
    {
        using var zip = Zip(
            ("b/rst.payments.json", """{"from":"b"}"""),
            ("a/rst.payments.json", """{"from":"a"}"""));

        var files = ConfigurationArchiveReader.Read(zip, Options).Files;

        files.Count.ShouldBe(2);
        files[0].Path.ShouldBe("a/rst.payments.json");
        files[0].Content!["from"]!.Value<string>().ShouldBe("a");
        files[1].ErrorCode.ShouldBe(ErrorCodes.DuplicateFile);
        files[1].Message!.ShouldContain("a/rst.payments.json");
    }

    [Fact]
    public void Read_RejectsFilesOverTheDocumentLimit()
    {
        using var zip = Zip(("rst.big.json", $"{{\"a\":\"{new string('x', 200)}\"}}"), ("rst.small.json", "{}"));

        var files = ConfigurationArchiveReader.Read(zip, new ConfigurationTransferOptions { MaxDocumentBytes = 100 }).Files;

        files.Single(file => file.Key.ToString() == "rst.big").ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files.Single(file => file.Key.ToString() == "rst.small").ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void Read_ParsesDatesLikeTheStoredDocuments()
    {
        using var zip = Zip(("rst.a.json", """{"at":"2026-10-14T01:30:12Z","text":"not a date"}"""));

        var content = ConfigurationArchiveReader.Read(zip, Options).Files[0].Content!;

        content["at"]!.Type.ShouldBe(JTokenType.Date);
        content["text"]!.Type.ShouldBe(JTokenType.String);
    }

    [Fact]
    public void Validate_RejectsWhatIsNotAZip()
    {
        using var stream = new MemoryStream("not a zip at all"u8.ToArray());

        var exception = Should.Throw<InvalidInputException>(() => ConfigurationArchiveReader.Validate(stream, Options));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_RejectsTooManyEntries()
    {
        using var zip = Zip(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));

        var exception = Should.Throw<InvalidInputException>(() => ConfigurationArchiveReader.Validate(zip, new ConfigurationTransferOptions { MaxArchiveEntries = 2 }));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_RejectsTooMuchUncompressedContent()
    {
        // well compressible: the zip is small, the declared sizes are not
        using var zip = Zip(("rst.a.json", new string(' ', 4000) + "{}"), ("rst.b.json", new string(' ', 4000) + "{}"));

        var options = new ConfigurationTransferOptions { MaxExtractedBytes = 6000, MaxDocumentBytes = 5000 };
        var exception = Should.Throw<InvalidInputException>(() => ConfigurationArchiveReader.Validate(zip, options));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_CountsOnlyConfigurationFilesAgainstTheExtractedLimit()
    {
        using var zip = Zip(("notes.txt", new string(' ', 8000)), ("rst.a.json", "{}"));

        Should.NotThrow(() => ConfigurationArchiveReader.Validate(zip, new ConfigurationTransferOptions { MaxExtractedBytes = 100, MaxDocumentBytes = 50 }));
    }

    [Fact]
    public void Validate_AcceptsAnEmptyZip()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        stream.Position = 0;
        Should.NotThrow(() => ConfigurationArchiveReader.Validate(stream, Options));
    }
}
