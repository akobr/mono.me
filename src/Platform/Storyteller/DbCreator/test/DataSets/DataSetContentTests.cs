using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetContentTests
{
    private static readonly string Folder = SetFiles.Folder("showcase");
    private static readonly DataSetTarget Target = new("fabrikam", "invoicing-suite", "default");

    [Fact]
    public void Read_FailsALineWhoseKeyAFileOrAnEarlierLineHolds()
    {
        var fileSystem = SetFiles.Create(
            Folder,
            ("set.json", "{}"),
            ("annotations/sbt.a.json", "{}"),
            ("annotations/sbt.broken.json", "{ not json"),
            ("annotations.jsonl", string.Join(
                "\n",
                """{"AnnotationKey":"sbt.a","Title":"Also in a file"}""",
                """{"AnnotationKey":"sbt.c"}""",
                """{"$count":3,"AnnotationKey":"sbt.g-{n}"}""",
                """{"AnnotationKey":"SBT.c"}""",
                """{"AnnotationKey":"sbt.broken"}""",
                """{"AnnotationKey":"sbt.g-2","Title":"Again"}""",
                """{"AnnotationKey":"sbt.d","Colour":"red"}""",
                """{"AnnotationKey":"sbt.d"}""")));

        var content = DataSetContent.Read(fileSystem, Folder, Target);

        content.Documents.Files.Count.ShouldBe(2);
        content.Documents.IgnoredFiles.ShouldBe(0);

        var lines = content.Lines.ShouldNotBeNull();
        lines.Annotations.Select(line => (line.Line, line.Key!.ToString(), line.ErrorCode)).ShouldBe(
        [
            (1, "sbt.a", ErrorCodes.DuplicateFile),
            (2, "sbt.c", null),
            (3, "sbt.g-1", null),
            (3, "sbt.g-2", null),
            (3, "sbt.g-3", null),
            (4, "sbt.c", ErrorCodes.DuplicateFile),
            (5, "sbt.broken", ErrorCodes.DuplicateFile),
            (6, "sbt.g-2", ErrorCodes.DuplicateFile),
            (7, "sbt.d", ErrorCodes.InvalidDocument),
            (8, "sbt.d", ErrorCodes.DuplicateFile),
        ]);
        lines.Annotations[0].Message.ShouldBe("The file 'annotations/sbt.a.json' already holds the annotation sbt.a.");
        lines.Annotations[5].Message.ShouldBe("Line 2 of annotations.jsonl already holds the annotation sbt.c.");
        lines.Annotations[7].Message.ShouldBe("Line 3 of annotations.jsonl already holds the annotation sbt.g-2.");
        lines.Annotations.Where(line => line.ErrorCode == ErrorCodes.DuplicateFile).ShouldAllBe(line => line.Annotation == null);
    }

    [Fact]
    public void Read_HasNoLinesWithoutAnnotationsJsonl()
    {
        var fileSystem = SetFiles.Create(Folder, ("set.json", "{}"), ("annotations/sbt.a.json", "{}"));

        var content = DataSetContent.Read(fileSystem, Folder, Target);

        content.Lines.ShouldBeNull();
        content.Documents.Files.ShouldHaveSingleItem().AnnotationKey!.ToString().ShouldBe("sbt.a");
    }
}
