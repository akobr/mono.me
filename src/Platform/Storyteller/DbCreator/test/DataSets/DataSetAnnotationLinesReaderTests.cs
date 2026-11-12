using System.Text.Json;
using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetAnnotationLinesReaderTests
{
    private static readonly DataSetTarget Target = new("fabrikam", "invoicing-suite", "preview");

    [Fact]
    public void Read_SkipsBlankLinesAndCommentsAndReadsALineAsAFile()
    {
        var lines = Read(
            "// the payments",
            string.Empty,
            """{"AnnotationKey":"rst.payments","Title":"Payments","Labels":["core"]}""",
            "   ",
            """  // indented comment""",
            """{"annotationkey":"unt.payments.ledger","unittype":"service","Description":"The ledger."}""");

        lines.Lines.ShouldBe(2);
        lines.GeneratorLines.ShouldBe(0);
        lines.Annotations.Select(annotation => (annotation.Line, annotation.Key!.ToString())).ShouldBe([(3, "rst.payments"), (6, "unt.payments.ledger")]);

        var payments = lines.Annotations[0].Annotation.ShouldBeOfType<Responsibility>();
        payments.Title.ShouldBe("Payments");
        payments.Labels.ShouldBe(["core"]);
        payments.ProjectName.ShouldBe("invoicing-suite");
        payments.ViewName.ShouldBe("preview");

        var ledger = lines.Annotations[1].Annotation.ShouldBeOfType<Unit>();
        ledger.UnitType.ShouldBe("service");
        ledger.Description.ShouldBe("The ledger.");
    }

    [Fact]
    public void Read_KeepsTheTextOfNumbersAndDatesStayStrings()
    {
        var lines = Read("""{"AnnotationKey":"sbt.acme","Values":{"amount":1.10,"big":123456789012345678901234567890,"at":"2026-01-01T00:00:00Z"}}""");

        var values = lines.Annotations.ShouldHaveSingleItem().Annotation!.Values!;
        ((JsonElement)values["amount"]).GetRawText().ShouldBe("1.10");
        ((JsonElement)values["big"]).GetRawText().ShouldBe("123456789012345678901234567890");
        ((JsonElement)values["at"]).ValueKind.ShouldBe(JsonValueKind.String);
        ((JsonElement)values["at"]).GetString().ShouldBe("2026-01-01T00:00:00Z");
    }

    [Fact]
    public void Read_ExpandsAGeneratorLineInEveryString()
    {
        var lines = Read(
            """{"$count":3,"AnnotationKey":"sbt.customer-{000}","Title":"Customer {n}","Labels":["gen-{n}","fixed"],"Values":{"code":"c{00}","nested":{"deep":["x{n}-{0}"]},"amount":7}}""");

        lines.Lines.ShouldBe(1);
        lines.GeneratorLines.ShouldBe(1);
        lines.Annotations.Select(annotation => annotation.Line).ShouldBe([1, 1, 1]);
        lines.Annotations.Select(annotation => annotation.Key!.ToString()).ShouldBe(["sbt.customer-001", "sbt.customer-002", "sbt.customer-003"]);

        var second = lines.Annotations[1].Annotation!;
        second.AnnotationKey.ShouldBe("sbt.customer-002");
        second.Title.ShouldBe("Customer 2");
        second.Labels.ShouldBe(["gen-2", "fixed"]);
        ((JsonElement)second.Values!["code"]).GetString().ShouldBe("c02");
        ((JsonElement)second.Values!["nested"]).GetRawText().ShouldBe("""{"deep":["x2-2"]}""");
        ((JsonElement)second.Values!["amount"]).GetRawText().ShouldBe("7");
    }

    [Fact]
    public void Read_StartsAGeneratorAtItsStartAndPadsToAtLeastTheWidth()
    {
        var lines = Read(
            """{"$start":0,"$count":2,"AnnotationKey":"sbt.s-{00}"}""",
            """{"$COUNT":1,"$Start":123,"AnnotationKey":"exe.s-{0}.billing.prod"}""");

        lines.GeneratorLines.ShouldBe(2);
        lines.Annotations.Select(annotation => annotation.Key!.ToString()).ShouldBe(["sbt.s-00", "sbt.s-01", "exe.s-123.billing.prod"]);
    }

    [Fact]
    public void Read_TakesALineWithoutCountLiterally()
    {
        var lines = Read("""{"AnnotationKey":"sbt.literal","Title":"Customer {n} of {000}"}""");

        lines.Annotations.ShouldHaveSingleItem().Annotation!.Title.ShouldBe("Customer {n} of {000}");
    }

    [Fact]
    public void Read_TakesTheTypeCodeInAnyCase()
    {
        var lines = Read("""{"AnnotationKey":"RST.payments"}""");

        var annotation = lines.Annotations.ShouldHaveSingleItem();
        annotation.Key!.ToString().ShouldBe("rst.payments");
        annotation.Annotation!.AnnotationKey.ShouldBe("rst.payments");
    }

    [Theory]
    [InlineData("""{"AnnotationKey":"sbt.a",""", ErrorCodes.InvalidDocument, "is not a JSON object", false)]
    [InlineData("""[1, 2]""", ErrorCodes.InvalidDocument, "is not a JSON object", false)]
    [InlineData("""{"Title":"no key"}""", ErrorCodes.InvalidDocument, "has no AnnotationKey", false)]
    [InlineData("""{"AnnotationKey":"foo.bar"}""", ErrorCodes.InvalidKey, "is not an annotation key", false)]
    [InlineData("""{"AnnotationKey":"sbt..a"}""", ErrorCodes.InvalidKey, "is not an annotation key", false)]
    [InlineData("""{"AnnotationKey":42}""", ErrorCodes.InvalidKey, "is not an annotation key", false)]
    [InlineData("""{"AnnotationKey":"sbt.a","Colour":"red"}""", ErrorCodes.InvalidDocument, "Unknown member 'Colour'", true)]
    [InlineData("""{"AnnotationKey":"sbt.a","UnitType":"service"}""", ErrorCodes.InvalidDocument, "Unknown member 'UnitType'", true)]
    [InlineData("""{"$count":0,"AnnotationKey":"sbt.a-{n}"}""", ErrorCodes.InvalidDocument, "$count must be a whole number of 1 or more, not 0", false)]
    [InlineData("""{"$count":"3","AnnotationKey":"sbt.a-{n}"}""", ErrorCodes.InvalidDocument, "$count must be a whole number", false)]
    [InlineData("""{"$count":1.5,"AnnotationKey":"sbt.a-{n}"}""", ErrorCodes.InvalidDocument, "$count must be a whole number", false)]
    [InlineData("""{"$count":2,"$start":-1,"AnnotationKey":"sbt.a-{n}"}""", ErrorCodes.InvalidDocument, "$start must be a whole number of 0 or more", false)]
    [InlineData("""{"$start":1,"AnnotationKey":"sbt.a"}""", ErrorCodes.InvalidDocument, "$start without $count", false)]
    public void Read_FailsALineThatCannotBeReadWithItsNumber(string line, string errorCode, string message, bool hasKey)
    {
        var lines = Read("""{"AnnotationKey":"rst.first"}""", "// between", line);

        lines.Lines.ShouldBe(2);
        var failed = lines.Annotations[1];
        failed.Line.ShouldBe(3);
        failed.Annotation.ShouldBeNull();
        failed.ErrorCode.ShouldBe(errorCode);
        failed.Message.ShouldNotBeNull().ShouldContain(message);
        (failed.Key is not null).ShouldBe(hasKey);
    }

    [Fact]
    public void Read_FailsEachGeneratedAnnotationThatIsNotValid()
    {
        // the padded index fits a name, the long title does not
        var lines = Read($$"""{"$count":2,"AnnotationKey":"sbt.g-{n}","Title":"{{new string('t', 300)}}"}""");

        lines.Annotations.Count.ShouldBe(2);
        lines.Annotations.ShouldAllBe(annotation => annotation.ErrorCode == ErrorCodes.InvalidAnnotation && annotation.Line == 1);
        lines.Annotations.Select(annotation => annotation.Key!.ToString()).ShouldBe(["sbt.g-1", "sbt.g-2"]);
    }

    [Fact]
    public void Read_RefusesMoreAnnotationsThanASetHolds()
    {
        Should.Throw<DataSetException>(() => Read("""{"$count":200001,"AnnotationKey":"sbt.g-{n}"}"""))
            .Message.ShouldContain("more than 200,000 annotations at line 1");

        // counted before any line is expanded: a line that cannot be read counts as one
        Should.Throw<DataSetException>(() => Read(
                """{"$count":199998,"AnnotationKey":"sbt.g-{n}"}""",
                "not json",
                "// a comment does not count",
                """{"AnnotationKey":"sbt.last"}""",
                """{"AnnotationKey":"sbt.over"}"""))
            .Message.ShouldContain("at line 5");
    }

    [Fact]
    public void Read_ReadsTheFileOfTheSetFolder()
    {
        var folder = SetFiles.Folder("lines");
        var fileSystem = SetFiles.Create(folder, ("annotations.jsonl", "﻿{\"AnnotationKey\":\"sbt.a\"}\r\n{\"$count\":2,\"AnnotationKey\":\"sbt.b-{n}\"}\r\n"));

        var lines = DataSetAnnotationLinesReader.Read(fileSystem, folder, Target);

        lines.ShouldNotBeNull().Annotations.Select(annotation => annotation.Key!.ToString()).ShouldBe(["sbt.a", "sbt.b-1", "sbt.b-2"]);
        DataSetAnnotationLinesReader.Read(SetFiles.Create(folder), folder, Target).ShouldBeNull();
    }

    [Fact]
    public void CountAnnotations_CountsWithoutExpandingAndRefusesWhatReadRefuses()
    {
        var folder = SetFiles.Folder("lines");

        DataSetAnnotationLinesReader.CountAnnotations(
                SetFiles.Create(folder, ("annotations.jsonl", "{\"AnnotationKey\":\"sbt.a\"}\n// a comment\n\nnot json\n{\"$count\":150000,\"AnnotationKey\":\"sbt.b-{n}\"}\n")),
                folder)
            .ShouldBe(150_002);
        DataSetAnnotationLinesReader.CountAnnotations(SetFiles.Create(folder), folder).ShouldBe(0);

        Should.Throw<DataSetException>(() => DataSetAnnotationLinesReader.CountAnnotations(
                SetFiles.Create(folder, ("annotations.jsonl", "{\"$count\":200001,\"AnnotationKey\":\"sbt.g-{n}\"}")),
                folder))
            .Message.ShouldContain("more than 200,000 annotations at line 1");
    }

    private static DataSetAnnotationLines Read(params string[] lines)
    {
        return DataSetAnnotationLinesReader.Read(new StringReader(string.Join("\n", lines)), Target);
    }
}
