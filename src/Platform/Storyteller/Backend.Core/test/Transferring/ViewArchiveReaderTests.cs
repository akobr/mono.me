using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewArchiveReaderTests
{
    private static readonly ViewTransferOptions Options = new();

    [Fact]
    public void Read_FindsConfigurationFilesInSubfolders()
    {
        using var zip = Zip(
            ("configs/", string.Empty),
            ("configs/rst.payments.conf.json", """{"a":1}"""),
            ("configs/subjects/sbt.shop.conf.json", """{"b":2}"""),
            ("usg.shop.payments.conf.json", """{"c":3}"""));

        var content = ViewArchiveReader.Read(zip, Options);

        content.Files.Select(file => file.AnnotationKey!.ToString()).ShouldBe(["rst.payments", "sbt.shop", "usg.shop.payments"]);
        content.Files.ShouldAllBe(file => file.Kind == ViewDocumentKind.Configuration);
        content.Files.Select(file => file.Path).ShouldBe(["configs/rst.payments.conf.json", "configs/subjects/sbt.shop.conf.json", "usg.shop.payments.conf.json"]);
        content.Files.ShouldAllBe(file => file.ErrorCode == null);
        content.IgnoredFiles.ShouldBe(0);
    }

    [Fact]
    public void Read_IgnoresVersionFilesTheManifestAndOtherFiles()
    {
        using var zip = Zip(
            ("rst.payments.conf.json", """{"a":1}"""),
            ("rst.payments.conf.1.json", """{"a":0}"""),
            ("export.json", "{}"),
            ("README.md", "# configurations"),
            ("notes.json", "{}"));

        var content = ViewArchiveReader.Read(zip, Options);

        content.Files.Count.ShouldBe(1);
        content.IgnoredFiles.ShouldBe(4);
    }

    [Fact]
    public void Read_IgnoresTheOtherKindsForNow()
    {
        // transitional: annotations, schemas and templates are read once the import takes every kind
        using var zip = Zip(
            ("rst.payments.conf.json", """{"a":1}"""),
            ("rst.payments.json", "{}"),
            ("rst.payments.schema.json", "{}"),
            ("exe.template.json", "{}"),
            ("exe.schema.json", "{}"));

        var content = ViewArchiveReader.Read(zip, Options);

        content.Files.Select(file => file.Path).ShouldBe(["rst.payments.conf.json"]);
        content.IgnoredFiles.ShouldBe(4);
    }

    [Fact]
    public void Read_SkipsHiddenAndMacFolders()
    {
        using var zip = Zip(
            (".git/HEAD", "ref: refs/heads/main"),
            (".git/rst.inside.conf.json", "{}"),
            ("repo/.git/config", "[core]"),
            (".github/rst.workflow.conf.json", "{}"),
            ("configs/.vscode/rst.settings.conf.json", "{}"),
            ("__MACOSX/._rst.payments.conf.json", "binary"),
            ("._rst.payments.conf.json", "binary"),
            ("rst.payments.conf.json", """{"a":1}"""));

        var content = ViewArchiveReader.Read(zip, Options);

        content.Files.Select(file => file.Path).ShouldBe(["rst.payments.conf.json"]);
        content.IgnoredFiles.ShouldBe(0);
    }

    [Fact]
    public void Read_ReportsInvalidDocuments()
    {
        using var zip = Zip(
            ("rst.array.conf.json", "[1,2]"),
            ("rst.broken.conf.json", """{"a":"""),
            ("rst.duplicate.conf.json", """{"a":1,"a":2}"""),
            ("rst.trailing.conf.json", """{"a":1} {"b":2}"""),
            ("rst.commented.conf.json", "// a comment\n{\"a\":1 /* inline */}\n// end"));

        var files = ViewArchiveReader.Read(zip, Options).Files.ToDictionary(file => file.AnnotationKey!.ToString());

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
            ("b/rst.payments.conf.json", """{"from":"b"}"""),
            ("a/rst.payments.conf.json", """{"from":"a"}"""));

        var files = ViewArchiveReader.Read(zip, Options).Files;

        files.Count.ShouldBe(2);
        files[0].Path.ShouldBe("a/rst.payments.conf.json");
        files[0].Content!["from"]!.Value<string>().ShouldBe("a");
        files[1].ErrorCode.ShouldBe(ErrorCodes.DuplicateFile);
        files[1].Message!.ShouldContain("a/rst.payments.conf.json");
    }

    [Fact]
    public void Read_TakesEncodedAndPlainSpellingsAsDuplicates()
    {
        using var zip = Zip(
            ("rst.payments.conf.json", """{"from":"plain"}"""),
            ("rst.pay%6Dents.conf.json", """{"from":"encoded"}"""));

        var files = ViewArchiveReader.Read(zip, Options).Files;

        files.Count.ShouldBe(2);
        files[0].Path.ShouldBe("rst.pay%6Dents.conf.json");
        files[0].ErrorCode.ShouldBeNull();
        files[1].ErrorCode.ShouldBe(ErrorCodes.DuplicateFile);
    }

    [Fact]
    public void Read_RejectsFilesOverTheDocumentLimit()
    {
        using var zip = Zip(("rst.big.conf.json", $"{{\"a\":\"{new string('x', 200)}\"}}"), ("rst.small.conf.json", "{}"));

        var files = ViewArchiveReader.Read(zip, new ViewTransferOptions { MaxDocumentBytes = 100 }).Files;

        files.Single(file => file.AnnotationKey!.ToString() == "rst.big").ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files.Single(file => file.AnnotationKey!.ToString() == "rst.small").ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void Read_ParsesDatesLikeTheStoredDocuments()
    {
        using var zip = Zip(("rst.a.conf.json", """{"at":"2026-10-14T01:30:12Z","text":"not a date"}"""));

        var content = ViewArchiveReader.Read(zip, Options).Files[0].Content!;

        content["at"]!.Type.ShouldBe(JTokenType.Date);
        content["text"]!.Type.ShouldBe(JTokenType.String);
    }

    [Fact]
    public void Validate_RejectsWhatIsNotAZip()
    {
        using var stream = new MemoryStream("not a zip at all"u8.ToArray());

        var exception = Should.Throw<InvalidInputException>(() => ViewArchiveReader.Validate(stream, Options));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_RejectsTooManyEntries()
    {
        using var zip = Zip(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));

        var exception = Should.Throw<InvalidInputException>(() => ViewArchiveReader.Validate(zip, new ViewTransferOptions { MaxArchiveEntries = 2 }));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_RejectsTooMuchUncompressedContent()
    {
        // well compressible: the zip is small, the declared sizes are not
        using var zip = Zip(("rst.a.conf.json", new string(' ', 4000) + "{}"), ("rst.b.conf.json", new string(' ', 4000) + "{}"));

        var options = new ViewTransferOptions { MaxExtractedBytes = 6000, MaxDocumentBytes = 5000 };
        var exception = Should.Throw<InvalidInputException>(() => ViewArchiveReader.Validate(zip, options));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_CountsOnlyDocumentFilesAgainstTheExtractedLimit()
    {
        using var zip = Zip(("notes.txt", new string(' ', 8000)), ("rst.a.conf.json", "{}"));

        Should.NotThrow(() => ViewArchiveReader.Validate(zip, new ViewTransferOptions { MaxExtractedBytes = 100, MaxDocumentBytes = 50 }));
    }

    [Fact]
    public void Validate_AcceptsAnEmptyZip()
    {
        using var stream = new MemoryStream();
        using (new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        stream.Position = 0;
        Should.NotThrow(() => ViewArchiveReader.Validate(stream, Options));
    }
}
