using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class AnnotationRulesTests
{
    [Fact]
    public void Validate_WithinTheLimits_Passes()
    {
        var annotation = Subject() with
        {
            Title = new string('t', AnnotationLimits.TitleMaxLength),
            Description = new string('d', AnnotationLimits.DescriptionMaxLength),
            DocumentationLink = "https://docs.example.com/" + new string('x', AnnotationLimits.DocumentationLinkMaxLength - 25),
        };

        Should.NotThrow(() => AnnotationRules.Validate(annotation));
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("Description")]
    [InlineData("DocumentationLink")]
    public void Validate_OverTheLimit_IsRejectedWithTheField(string field)
    {
        var annotation = field switch
        {
            "Title" => Subject() with { Title = new string('t', AnnotationLimits.TitleMaxLength + 1) },
            "Description" => Subject() with { Description = new string('d', AnnotationLimits.DescriptionMaxLength + 1) },
            _ => Subject() with { DocumentationLink = "https://docs.example.com/" + new string('x', AnnotationLimits.DocumentationLinkMaxLength) },
        };

        var exception = Should.Throw<InvalidInputException>(() => AnnotationRules.Validate(annotation));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
        exception.Message.ShouldContain(field);
    }

    [Theory]
    [InlineData("ftp://docs.example.com/a")]
    [InlineData("docs.example.com/a")]
    [InlineData("https://docs.example.com/a b")]
    public void Validate_LinkThatIsNotAnAbsoluteHttpUrl_IsRejected(string link)
    {
        Should.Throw<InvalidInputException>(() => AnnotationRules.Validate(Subject() with { DocumentationLink = link }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://docs.example.com")]
    public void Validate_EmptyOrHttpLink_Passes(string? link)
    {
        Should.NotThrow(() => AnnotationRules.Validate(Subject() with { DocumentationLink = link }));
    }

    [Fact]
    public void ValidateAll_ListsEveryInvalidKey()
    {
        var invalid = new[]
        {
            Subject("sbt.first") with { Title = new string('t', 201) },
            Subject("sbt.second"),
            Subject("sbt.third") with { DocumentationLink = "nope" },
        };

        var exception = Should.Throw<InvalidInputException>(() => AnnotationRules.ValidateAll(invalid));

        exception.Message.ShouldContain("sbt.first");
        exception.Message.ShouldContain("sbt.third");
        exception.Message.ShouldNotContain("sbt.second");
    }

    private static Subject Subject(string key = "sbt.northwind")
    {
        return new Subject
        {
            ProjectName = "billing",
            ViewName = "default",
            AnnotationKey = key,
            Name = key.Split('.')[1],
            AnnotationType = AnnotationType.Subject,
        };
    }
}
