using System.Diagnostics;
using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// The name queries of the typed lists: exact, %contains% or a ^pattern$, bounded and refused with 400 InvalidQuery.
public class NameQueryTests
{
    // Nested repetitions: seconds of backtracking per 30-character name on the backtracking engine.
    private const string CatastrophicPattern = "^([a-zA-Z0-9_-]+)+!";

    // 26 branches of .*X.{60} (242 characters): too many states for the lazy automaton of the non-backtracking
    // engine, which falls back to a mode of milliseconds per long name that does not check the match timeout.
    private static readonly string StateHeavyPattern = "^(?:" + string.Join("|", Enumerable.Range('a', 26).Select(letter => $".*{(char)letter}.{{60}}")) + "|zz)$";

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
        exception.Message.ShouldStartWith($"The unitNameQuery '{text}' is not supported by name queries: ");
    }

    // The engine refuses a pattern whose automaton would be too large; its message says so.
    [Theory]
    [InlineData("^.{0,2000}$")]
    [InlineData("^(a{1,100}){1,100}$")]
    public void Parse_TooLargeAutomaton_IsInvalidQuery(string text)
    {
        var exception = Should.Throw<InvalidInputException>(() => NameQuery.Parse(text, "nameQuery"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldStartWith($"The nameQuery '{text}' is not supported by name queries: ");
        exception.Message.ShouldContain("10000");
    }

    [Fact]
    public void Parse_PatternOverTheMaximalLength_IsInvalidQuery()
    {
        var text = "^" + new string('a', NameQuery.PatternMaxLength);

        var exception = Should.Throw<InvalidInputException>(() => NameQuery.Parse(text, "nameQuery"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe($"The nameQuery pattern has {NameQuery.PatternMaxLength + 1} characters, at most {NameQuery.PatternMaxLength} are allowed.");
        NameQuery.Parse("^" + new string('a', NameQuery.PatternMaxLength - 1), "nameQuery").ShouldNotBeNull();
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

    // A match that runs into the timeout is refused, not a 500.
    [Fact]
    public void IsMatch_OverTheTimeout_IsInvalidQuery()
    {
        var query = NameQuery.Parse("^([a-z]+)+!", "contextNameQuery", TimeSpan.FromTicks(1), NameQuery.MatchBudget)!;

        var exception = Should.Throw<InvalidInputException>(() => query.IsMatch(new string('a', 5_000_000)));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe("The contextNameQuery took too long to match the names; simplify the pattern.");
    }

    // The budget bounds the page where the timeout of one match does not fire: in the slow mode of the engine this
    // pattern takes about 5 ms per name of 63 characters, about 5 s per page.
    [Fact]
    public void IsMatch_StateHeavyPatternOverAPage_RunsOutOfTheBudget()
    {
        var query = NameQuery.Parse(StateHeavyPattern, "nameQuery")!;
        // varied letters keep the automaton growing; a name of one repeated letter would stay fast
        var random = new Random(42);
        var names = Enumerable.Range(0, 1000).Select(_ => new string(Enumerable.Range(0, 63).Select(_ => (char)('a' + random.Next(26))).ToArray())).ToList();
        var watch = Stopwatch.StartNew();

        var exception = Should.Throw<InvalidInputException>(() => names.Count(query.IsMatch));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidQuery);
        exception.Message.ShouldBe("The nameQuery took too long to match the names; simplify the pattern.");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    // Names have at most 63 characters, so an ordinary pattern stays far within the budget over a page.
    [Fact]
    public void IsMatch_OrdinaryPatternOverAPage_StaysWithinTheBudget()
    {
        var query = NameQuery.Parse("^(north|south)-[a-z0-9-]+-eu$", "subjectNameQuery")!;
        var names = Enumerable.Range(0, 1000).Select(index => $"north-{index:d4}-" + new string('x', 50) + "-eu").ToList();

        names.Count(query.IsMatch).ShouldBe(1000);
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
