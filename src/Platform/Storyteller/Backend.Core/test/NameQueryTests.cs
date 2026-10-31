using System.Diagnostics;
using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The name queries of the typed lists: exact, %contains% or a ^pattern$, bounded and refused with 400 InvalidQuery.
public class NameQueryTests
{
    // Nested repetitions: seconds of backtracking per 30-character name on the backtracking engine.
    private const string CatastrophicPattern = "^([a-zA-Z0-9_-]+)+!";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithoutText_IsNoQuery(string? text)
    {
        NameQuery.Parse(text, "nameQuery").ShouldBeNull();
    }

    [Fact]
    public void Parse_Exact_IsTrimmedAndCaseSensitive()
    {
        var query = NameQuery.Parse("  northwind ", "nameQuery")!;

        query.Kind.ShouldBe(NameQueryKind.Exact);
        query.IsExact.ShouldBeTrue();
        query.Value.ShouldBe("northwind");
        query.Parameter.ShouldBe("nameQuery");
        query.IsMatch("northwind").ShouldBeTrue();
        query.IsMatch("Northwind").ShouldBeFalse();
        query.IsMatch("northwind-eu").ShouldBeFalse();
    }

    [Theory]
    [InlineData("%north%")]
    [InlineData("%north")]
    [InlineData("north%")]
    [InlineData("%%north%%")]
    public void Parse_Contains_StripsTheMarkers(string text)
    {
        var query = NameQuery.Parse(text, "nameQuery")!;

        query.Kind.ShouldBe(NameQueryKind.Contains);
        query.IsExact.ShouldBeFalse();
        query.Value.ShouldBe("north");
        query.IsMatch("northwind").ShouldBeTrue();
        query.IsMatch("far-north").ShouldBeTrue();
        query.IsMatch("North").ShouldBeFalse();
        query.IsMatch("contoso").ShouldBeFalse();
    }

    [Fact]
    public void Parse_ContainsOfNothing_MatchesEveryName()
    {
        var query = NameQuery.Parse("%", "nameQuery")!;

        query.Kind.ShouldBe(NameQueryKind.Contains);
        query.IsMatch("northwind").ShouldBeTrue();
        query.IsMatch(string.Empty).ShouldBeTrue();
    }

    [Theory]
    [InlineData("^north", "northwind", true)]
    [InlineData("^north", "far-north", false)]
    [InlineData("wind$", "northwind", true)]
    [InlineData("wind$", "windows", false)]
    [InlineData("^(north|south)-eu$", "south-eu", true)]
    [InlineData("^(north|south)-eu$", "south-eu2", false)]
    [InlineData("^North", "northwind", false)]
    [InlineData("^(?i)North", "northwind", true)]
    public void Parse_Pattern_MatchesTheRegularExpression(string text, string name, bool matches)
    {
        var query = NameQuery.Parse(text, "nameQuery")!;

        query.Kind.ShouldBe(NameQueryKind.Pattern);
        query.Value.ShouldBe(text);
        query.IsMatch(name).ShouldBe(matches);
    }

    // The pattern check comes first, as before: a query that ends with $ is a pattern even with a leading %.
    [Fact]
    public void Parse_PercentAndDollar_IsAPattern()
    {
        var query = NameQuery.Parse("%wind$", "nameQuery")!;

        query.Kind.ShouldBe(NameQueryKind.Pattern);
        query.IsMatch("%wind").ShouldBeTrue();
        query.IsMatch("northwind").ShouldBeFalse();
    }

    [Fact]
    public void Parse_InvalidPattern_IsInvalidQuery()
    {
        var exception = Should.Throw<InvalidInputException>(() => NameQuery.Parse("^[", "subjectNameQuery"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldStartWith("The subjectNameQuery is not a valid regular expression: ");
    }

    // The non-backtracking engine has no backreferences, lookarounds, atomic groups or conditionals.
    [Theory]
    [InlineData(@"^(a)\1$")]
    [InlineData("^(?=north)")]
    [InlineData("^(?<!x)north")]
    [InlineData("^(?>north)")]
    [InlineData("^(?(north)north|south)$")]
    public void Parse_UnsupportedConstruct_IsInvalidQuery(string text)
    {
        var exception = Should.Throw<InvalidInputException>(() => NameQuery.Parse(text, "unitNameQuery"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe($"The unitNameQuery '{text}' uses a construct name queries don't support (for example backreferences, lookarounds, atomic groups or conditionals).");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(1000)]
    public void IsMatch_CatastrophicPattern_AnswersWithoutBacktracking(int length)
    {
        var query = NameQuery.Parse(CatastrophicPattern, "nameQuery")!;
        var watch = Stopwatch.StartNew();

        var matches = query.IsMatch(new string('a', length));

        matches.ShouldBeFalse();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    // The timeout stays as a second bound; a match that runs into it is refused, not a 500.
    [Fact]
    public void IsMatch_OverTheTimeout_IsInvalidQuery()
    {
        var query = NameQuery.Parse("^([a-z]+)+!", "contextNameQuery", TimeSpan.FromTicks(1))!;

        var exception = Should.Throw<InvalidInputException>(() => query.IsMatch(new string('a', 5_000_000)));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe("The contextNameQuery took too long to match a name; simplify the pattern.");
    }

    [Fact]
    public void IsMatch_Null_IsNoMatch()
    {
        NameQuery.Parse("northwind", "nameQuery")!.IsMatch(null).ShouldBeFalse();
        NameQuery.Parse("%north%", "nameQuery")!.IsMatch(null).ShouldBeFalse();
        NameQuery.Parse("^north", "nameQuery")!.IsMatch(null).ShouldBeFalse();
    }

    [Fact]
    public void ToCondition_FiltersTheModelsByTheName()
    {
        var query = NameQuery.Parse("^in", "responsibilityNameQuery")!;

        var condition = query.ToCondition<Usage>(u => u.ResponsibilityName);
        var predicate = condition.Predicate.Compile();

        condition.Against.ShouldBe(AnnotationType.Usage);
        predicate(Usage("usg.northwind.invoicing")).ShouldBeTrue();
        predicate(Usage("usg.northwind.shipping")).ShouldBeFalse();
    }

    private static Usage Usage(string key)
    {
        return (Usage)AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "billing", "default");
    }
}
