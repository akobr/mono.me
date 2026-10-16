using System.IO.Compression;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

public class ViewArchiveReaderTests
{
    private static readonly ViewTransferOptions Options = new();

    // One file of every kind, flat.
    private static readonly (string Path, string Content)[] Documents =
    [
        ("rst.payments.json", """{"Title":"Payments"}"""),
        ("exe.schema.json", """{"type":"object"}"""),
        ("sbt.northwind.exe.schema.json", """{"required":["timeout"]}"""),
        ("rst.payments.schema.json", """{"required":["retries"]}"""),
        ("exe.template.json", """{"timeout":30}"""),
        ("rst.payments.conf.json", """{"retries":2}"""),
    ];

    [Fact]
    public void Read_TakesEveryKindByItsName()
    {
        using var zip = Zip(Documents);

        var content = ViewArchiveReader.Read(zip, Options);

        content.Files.Select(file => (file.Path, file.Kind, file.AnnotationType, file.AnnotationKey?.ToString())).ShouldBe(
        [
            ("exe.schema.json", ViewDocumentKind.TypeSchema, "exe", null),
            ("exe.template.json", ViewDocumentKind.Template, "exe", null),
            ("rst.payments.conf.json", ViewDocumentKind.Configuration, null, "rst.payments"),
            ("rst.payments.json", ViewDocumentKind.Annotation, null, "rst.payments"),
            ("rst.payments.schema.json", ViewDocumentKind.AnnotationSchema, null, "rst.payments"),
            ("sbt.northwind.exe.schema.json", ViewDocumentKind.DescendantTypeSchema, "exe", "sbt.northwind"),
        ]);
        content.Files.ShouldAllBe(file => file.ErrorCode == null && file.Message == null);
        content.Files.Single(file => file.Kind == ViewDocumentKind.Annotation).Content!["Title"]!.Value<string>().ShouldBe("Payments");
        content.Files.Single(file => file.Kind == ViewDocumentKind.Template).Content!["timeout"]!.Value<int>().ShouldBe(30);
        content.IgnoredFiles.ShouldBe(0);
        content.ExcludedFiles.ShouldBe(0);
    }

    [Fact]
    public void Read_GivesTheSameFilesForAFlatZipAndForeignFolders()
    {
        string[] folders = ["configurations", "x/y/z", "annotations", "schemas/old", "templates", "my export"];
        using var flat = Zip(Documents);
        using var nested = Zip(Documents.Select((document, index) => ($"{folders[index]}/{document.Path}", document.Content)).ToArray());

        var fromFlat = ViewArchiveReader.Read(flat, Options);
        var fromFolders = ViewArchiveReader.Read(nested, Options);

        Describe(fromFolders).ShouldBe(Describe(fromFlat));
        fromFolders.Files.Count.ShouldBe(6);
        fromFolders.Files.Single(file => file.Kind == ViewDocumentKind.Annotation).Path.ShouldBe("configurations/rst.payments.json");
        fromFolders.Files.Single(file => file.Kind == ViewDocumentKind.Configuration).Path.ShouldBe("my export/rst.payments.conf.json");
        fromFolders.IgnoredFiles.ShouldBe(fromFlat.IgnoredFiles);
    }

    [Fact]
    public void Read_SkipsHiddenAndMacPathsWithoutCountingThem()
    {
        using var zip = Zip(
            (".git/HEAD", "ref: refs/heads/main"),
            (".git/rst.inside.json", "{}"),
            ("repo/.git/config", "[core]"),
            (".github/workflows/exe.template.json", "{}"),
            ("export/.vscode/rst.settings.conf.json", "{}"),
            (".hidden/rst.payments.schema.json", "{}"),
            ("annotations/.DS_Store", "binary"),
            ("annotations/._rst.payments.json", "binary"),
            ("__MACOSX/annotations/._rst.payments.json", "binary"),
            ("__MACOSX/rst.payments.conf.json", "{}"),
            ("annotations/", string.Empty),
            ("annotations/rst.payments.json", "{}"));

        var content = ViewArchiveReader.Read(zip, Options);

        content.Files.Select(file => file.Path).ShouldBe(["annotations/rst.payments.json"]);
        content.IgnoredFiles.ShouldBe(0);
        content.ExcludedFiles.ShouldBe(0);
    }

    [Fact]
    public void Read_CountsExcludedAndIgnoredFiles()
    {
        // ignored: the version files of included and excluded kinds, a version of an annotation (not a document),
        // export.json and other names
        using var zip = Zip(
            ("annotations/rst.payments.json", "{}"),
            ("exe.schema.json", "{}"),
            ("rst.payments.schema.json", "{}"),
            ("sbt.northwind.exe.schema.json", "{}"),
            ("exe.template.json", "{}"),
            ("rst.payments.conf.json", "{}"),
            ("exe.template.3.json", "{}"),
            ("rst.payments.conf.1.json", "{}"),
            ("exe.schema.2.json", "{}"),
            ("rst.payments.3.json", "{}"),
            ("export.json", "{}"),
            ("README.md", "# view"),
            ("notes.json", "{}"),
            ("xyz.payments.json", "{}"));

        var content = ViewArchiveReader.Read(zip, Options, [ViewArchiveContent.Templates, ViewArchiveContent.Configurations]);

        content.Files.Select(file => file.Path).ShouldBe(["exe.template.json", "rst.payments.conf.json"]);
        content.ExcludedFiles.ShouldBe(4);
        content.IgnoredFiles.ShouldBe(8);
    }

    [Fact]
    public void Read_NullOrEmptyIncludeTakesEveryKind()
    {
        using var first = Zip(Documents);
        using var second = Zip(Documents);

        ViewArchiveReader.Read(first, Options, null).Files.Count.ShouldBe(6);
        ViewArchiveReader.Read(second, Options, []).Files.Count.ShouldBe(6);
    }

    [Fact]
    public void Read_NeverDecompressesExcludedFiles()
    {
        // the annotation breaks every limit, but it is left out, so it is only counted
        using var zip = Zip(("rst.big.json", $"{{\"Title\":\"{new string('x', 400)}\""), ("rst.payments.conf.json", "{}"));
        var options = new ViewTransferOptions { MaxExtractedBytes = 100, MaxDocumentBytes = 100 };

        var content = ViewArchiveReader.Read(zip, options, [ViewArchiveContent.Configurations]);

        content.Files.Select(file => file.Path).ShouldBe(["rst.payments.conf.json"]);
        content.ExcludedFiles.ShouldBe(1);

        zip.Position = 0;
        var exception = Should.Throw<InvalidInputException>(() => ViewArchiveReader.Read(zip, options));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Read_AppliesTheExtractedLimitToTheIncludedFilesTogether()
    {
        var body = $"{{\"a\":\"{new string('x', 50)}\"}}";
        using var zip = Zip(("rst.a.json", body), ("exe.template.json", body), ("rst.a.conf.json", body));
        var options = new ViewTransferOptions { MaxExtractedBytes = 150 };

        Should.Throw<InvalidInputException>(() => ViewArchiveReader.Read(zip, options)).ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);

        zip.Position = 0;
        ViewArchiveReader.Read(zip, options, [ViewArchiveContent.Annotations, ViewArchiveContent.Configurations]).Files.Count.ShouldBe(2);
    }

    [Fact]
    public void Read_FindsDuplicatesAcrossFoldersAndEncodings()
    {
        using var zip = Zip(
            ("templates/exe.template.json", """{"from":"templates"}"""),
            ("exe.template.json", """{"from":"root"}"""),
            ("backup/EXE.Template.json", """{"from":"backup"}"""),
            ("annotations/rst.payments.json", """{"Title":"folder"}"""),
            ("rst.pay%6Dents.json", """{"Title":"encoded"}"""),
            ("schemas/sbt.northwind.exe.schema.json", "{}"),
            ("sbt.north%77ind.EXE.schema.json", "{}"),
            ("configurations/rst.payments.conf.json", "{}"));

        var files = ViewArchiveReader.Read(zip, Options).Files;

        files.Select(file => (file.Path, file.ErrorCode)).ShouldBe(
        [
            ("annotations/rst.payments.json", null),
            ("backup/EXE.Template.json", null),
            ("configurations/rst.payments.conf.json", null),
            ("exe.template.json", ErrorCodes.DuplicateFile),
            ("rst.pay%6Dents.json", ErrorCodes.DuplicateFile),
            ("sbt.north%77ind.EXE.schema.json", null),
            ("schemas/sbt.northwind.exe.schema.json", ErrorCodes.DuplicateFile),
            ("templates/exe.template.json", ErrorCodes.DuplicateFile),
        ]);

        var byPath = files.ToDictionary(file => file.Path);
        byPath["backup/EXE.Template.json"].Content!["from"]!.Value<string>().ShouldBe("backup");
        byPath["exe.template.json"].Message.ShouldBe("The file 'backup/EXE.Template.json' already holds the template exe.");
        byPath["exe.template.json"].Content.ShouldBeNull();
        byPath["rst.pay%6Dents.json"].Message.ShouldBe("The file 'annotations/rst.payments.json' already holds the annotation rst.payments.");
        byPath["rst.pay%6Dents.json"].AnnotationKey!.ToString().ShouldBe("rst.payments");
        byPath["schemas/sbt.northwind.exe.schema.json"].Message.ShouldBe(
            "The file 'sbt.north%77ind.EXE.schema.json' already holds the exe schema below sbt.northwind.");
    }

    [Fact]
    public void Read_FailsAnInvalidDescendantTypeWithoutReadingIt()
    {
        using var zip = Zip(("schemas/exe.s.r.c.rst.schema.json", "not even JSON"));

        var file = ViewArchiveReader.Read(zip, Options).Files.Single();

        file.Kind.ShouldBe(ViewDocumentKind.DescendantTypeSchema);
        file.AnnotationType.ShouldBe("rst");
        file.AnnotationKey!.ToString().ShouldBe("exe.s.r.c");
        file.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        file.Message.ShouldNotBeNullOrEmpty();
        file.Content.ShouldBeNull();
    }

    [Fact]
    public void Read_ReportsInvalidDocuments()
    {
        using var zip = Zip(
            ("rst.array.conf.json", "[1,2]"),
            ("rst.broken.json", """{"Title":"""),
            ("exe.template.json", """{"a":1,"a":2}"""),
            ("exe.schema.json", """{"a":1} {"b":2}"""),
            ("rst.commented.conf.json", "// a comment\n{\"a\":1 /* inline */}\n// end"),
            ("rst.commented.json", "/* exported */ {\"Title\":\"x\"}"));

        var files = ViewArchiveReader.Read(zip, Options).Files.ToDictionary(file => file.Path);

        files["rst.array.conf.json"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["rst.broken.json"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["exe.template.json"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["exe.schema.json"].ErrorCode.ShouldBe(ErrorCodes.InvalidDocument);
        files["exe.schema.json"].Message!.ShouldStartWith("The file is not a JSON object: ");
        files["rst.commented.conf.json"].ErrorCode.ShouldBeNull();
        files["rst.commented.conf.json"].Content!["a"]!.Value<int>().ShouldBe(1);
        files["rst.commented.json"].ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void Read_RejectsFilesOverTheDocumentLimitOfEveryKind()
    {
        var big = $"{{\"a\":\"{new string('x', 200)}\"}}";
        using var zip = Zip(Documents.Select(document => (document.Path, big)).Append(("rst.small.conf.json", "{}")).ToArray());

        var files = ViewArchiveReader.Read(zip, new ViewTransferOptions { MaxDocumentBytes = 100 }).Files;

        files.Where(file => file.Path != "rst.small.conf.json").ShouldAllBe(file => file.ErrorCode == ErrorCodes.InvalidDocument);
        files.Where(file => file.Path != "rst.small.conf.json").Select(file => file.Kind).Distinct().Count().ShouldBe(6);
        files.Single(file => file.Path == "rst.small.conf.json").ErrorCode.ShouldBeNull();
    }

    [Fact]
    public void Read_ParsesDatesLikeTheReadsOfEachKind()
    {
        const string Dated = """{"at":"2026-10-14T01:30:12Z","text":"not a date"}""";
        using var zip = Zip(
            ("rst.a.conf.json", Dated),
            ("exe.template.json", Dated),
            ("exe.schema.json", Dated),
            ("rst.a.json", """{"Values":{"at":"2026-10-14T01:30:12Z"},"ValidFrom":"2026-10-14T01:30:12+02:00"}"""));

        var files = ViewArchiveReader.Read(zip, Options).Files.ToDictionary(file => file.Path);

        // configurations, templates and schemas are read by Newtonsoft, annotations by System.Text.Json
        files["rst.a.conf.json"].Content!["at"]!.Type.ShouldBe(JTokenType.Date);
        files["rst.a.conf.json"].Content!["text"]!.Type.ShouldBe(JTokenType.String);
        files["exe.template.json"].Content!["at"]!.Type.ShouldBe(JTokenType.Date);
        files["exe.schema.json"].Content!["at"]!.Type.ShouldBe(JTokenType.Date);
        files["rst.a.json"].Content!["Values"]!["at"]!.Type.ShouldBe(JTokenType.String);
        files["rst.a.json"].Content!["ValidFrom"]!.Value<string>().ShouldBe("2026-10-14T01:30:12+02:00");
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
    public void Validate_RejectsTooMuchUncompressedContentOfAnyKind()
    {
        // well compressible: the zip is small, the declared sizes are not
        using var zip = Zip(("rst.a.json", new string(' ', 4000) + "{}"), ("exe.template.json", new string(' ', 4000) + "{}"));

        var options = new ViewTransferOptions { MaxExtractedBytes = 6000, MaxDocumentBytes = 5000 };
        var exception = Should.Throw<InvalidInputException>(() => ViewArchiveReader.Validate(zip, options));
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidArchive);
    }

    [Fact]
    public void Validate_CountsOnlyDocumentFilesAgainstTheExtractedLimit()
    {
        using var zip = Zip(
            ("notes.txt", new string(' ', 8000)),
            ("rst.a.conf.1.json", new string(' ', 8000) + "{}"),
            (".git/objects/pack/pack.pack", new string(' ', 8000)),
            ("rst.a.conf.json", "{}"));

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

    // The files without their paths, by document.
    private static List<(ViewDocumentKind Kind, string? Type, string? Key, string? Content, string? ErrorCode)> Describe(ViewArchiveDocuments content)
    {
        return content.Files
            .Select(file => (file.Kind, file.AnnotationType, file.AnnotationKey?.ToString(), file.Content?.ToString(Formatting.None), file.ErrorCode))
            .OrderBy(file => file.Kind)
            .ThenBy(file => file.AnnotationType, StringComparer.Ordinal)
            .ThenBy(file => file.Item3, StringComparer.Ordinal)
            .ToList();
    }
}
