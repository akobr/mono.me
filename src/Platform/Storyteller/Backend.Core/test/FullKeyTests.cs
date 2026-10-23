namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class FullKeyTests
{
    [Fact]
    public void TryParse_ExecutionKey_HasEveryPart()
    {
        FullKey.TryParse("@house.main.default.exe.northwind.invoicing.production", out var key).ShouldBeTrue();

        key!.OrganizationName.ShouldBe("house");
        key.ProjectName.ShouldBe("main");
        key.ViewName.ShouldBe("default");
        key.Annotation.Type.ShouldBe(AnnotationType.Execution);
        key.Annotation.ToString().ShouldBe("exe.northwind.invoicing.production");
    }

    [Fact]
    public void TryParse_UnitOfExecutionKey_ParsesEightSegments()
    {
        FullKey.TryParse("@house.main.default.uxe.northwind.invoicing.production.nightly-export", out var key).ShouldBeTrue();

        key!.Annotation.Type.ShouldBe(AnnotationType.UnitOfExecution);
        key.Annotation.SubjectName.ShouldBe("northwind");
        key.Annotation.ResponsibilityName.ShouldBe("invoicing");
        key.Annotation.ContextName.ShouldBe("production");
        key.Annotation.UnitName.ShouldBe("nightly-export");
        key.ViewName.ShouldBe("default");
    }

    [Theory]
    [InlineData("@house.main.default.exe.northwind.invoicing.production.extra")]
    [InlineData("@house.main.default.uxe.northwind.invoicing.production.export.extra")]
    [InlineData("@house.main.default.xyz.northwind")]
    [InlineData("house.main.default.rst.invoicing")]
    [InlineData("@.main.default.rst.invoicing")]
    [InlineData("@house..default.rst.invoicing")]
    [InlineData("@house.main.default.rst.invoicing.")]
    [InlineData("@house.main.default.rst")]
    [InlineData("@house.main.default")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_InvalidKey_Fails(string text)
    {
        FullKey.TryParse(text, out var key).ShouldBeFalse();
        key.ShouldBeNull();
    }

    [Theory]
    [InlineData("house.main.default.rst.invoicing", "must start with '@'")]
    [InlineData("@.main.default.rst.invoicing", "non-empty")]
    [InlineData("@house.main.default.xyz.northwind", "'xyz' is not an annotation type code")]
    [InlineData("@house.main.default.exe.northwind.invoicing.production.extra", "is not a valid annotation key")]
    [InlineData("@house.main.default.uxe.a.b.c.d.e", "2 to 5 segments")]
    public void Parse_InvalidKey_ThrowsFormatExceptionWithTheReason(string text, string reason)
    {
        var exception = Should.Throw<FormatException>(() => FullKey.Parse(text));

        exception.Message.ShouldContain(reason);
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => FullKey.Parse(null!)).ParamName.ShouldBe("text");
        FullKey.TryParse(null!, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("rst.invoicing")]
    [InlineData("sbt.northwind")]
    [InlineData("unt.invoicing.export")]
    [InlineData("usg.northwind.invoicing")]
    [InlineData("cnt.northwind.production")]
    [InlineData("exe.northwind.invoicing.production")]
    [InlineData("uxe.northwind.invoicing.production.export")]
    public void Create_ToString_ParsesBackToTheSameText(string annotationKey)
    {
        var text = FullKey.Create(AnnotationKey.Parse(annotationKey), "house", "main", "default").ToString();

        FullKey.Parse(text).ToString().ShouldBe(text);
        text.ShouldBe($"@house.main.default.{annotationKey}");
    }
}
