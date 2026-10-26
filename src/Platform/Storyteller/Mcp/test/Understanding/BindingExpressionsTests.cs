using _42.Platform.Storyteller.Mcp.Understanding;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class BindingExpressionsTests
{
    [Theory]
    [InlineData("@db.host", BindingForm.Path, "db.host", "default")]
    [InlineData("@(db.password, primaryVault)", BindingForm.Sourced, "db.password", "primaryVault")]
    [InlineData("@( db . password , secret )", BindingForm.Sourced, "db.password", "secret")]
    [InlineData("@secret(db.password)", BindingForm.SourceCall, "db.password", "secret")]
    [InlineData("@public(db.host)", BindingForm.SourceCall, "db.host", "public")]
    [InlineData("@[https://@host.value:@port.value/api]", BindingForm.Interpolation, "host.value", "default")]
    [InlineData("@{(@base.value + 10) * 2}", BindingForm.Math, "base.value", "default")]
    [InlineData("@myFunction(\"literal\", @another.path)", BindingForm.Function, "another.path", "default")]
    [InlineData("@primaryVault(db.password)", BindingForm.SourceCall, "db.password", "primaryVault")]
    public void Parse_FindsTheFormAndTheFirstReference(string expression, BindingForm form, string key, string source)
    {
        var parsed = BindingExpressions.Parse(expression);

        parsed.Form.ShouldBe(form);
        parsed.References[0].ShouldBe(new BindingReference(key, source));
    }

    [Fact]
    public void Parse_CollectsNestedReferences()
    {
        var parsed = BindingExpressions.Parse("@myFunction(some.path, \"lit\\\"eral\", @another.path, @(x.y, secret))");

        parsed.References.ShouldBe(
        [
            new BindingReference("another.path", "default"),
            new BindingReference("x.y", "secret"),
            new BindingReference("some.path", "default"),
        ]);
    }

    [Fact]
    public void Parse_ConfigAndAnnotation_ReadThisDocumentAndAnnotationValues()
    {
        BindingExpressions.Parse("@config(\"/maxPrice\")").ShouldSatisfyAllConditions(
            parsed => parsed.Form.ShouldBe(BindingForm.Config),
            parsed => parsed.ReadsDocument.ShouldBeTrue(),
            parsed => parsed.References.ShouldBeEmpty());
        BindingExpressions.Parse("@annotation(\"/team\", \"Subject\")").ReadsAnnotations.ShouldBeTrue();
        BindingExpressions.Parse("@[@config(\"/a\") and @annotation(\"/b\")]").ShouldSatisfyAllConditions(
            parsed => parsed.ReadsDocument.ShouldBeTrue(),
            parsed => parsed.ReadsAnnotations.ShouldBeTrue());
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@ db.host")]
    [InlineData("@(db-password, vault)")]
    [InlineData("@[unterminated")]
    [InlineData("@{1 + x}")]
    [InlineData("@db.host trailing")]
    [InlineData("@db.")]
    public void Parse_Malformed_IsInvalid(string expression)
    {
        var parsed = BindingExpressions.Parse(expression);

        parsed.Form.ShouldBe(BindingForm.Invalid);
        parsed.References.ShouldBeEmpty();
        parsed.ExceedsLimits.ShouldBeFalse();
    }

    // Parser.ParseMath: expression := term (('+' | '-') term)*, term := factor (('*' | '/' | '%') factor)*,
    // factor := number | statement | '(' expression ')'
    [Theory]
    [InlineData("@{}")]
    [InlineData("@{1 +}")]
    [InlineData("@{-1}")]
    [InlineData("@{* 2}")]
    [InlineData("@{(1}")]
    [InlineData("@{1)}")]
    [InlineData("@{1 2}")]
    [InlineData("@{()}")]
    [InlineData("@{.5}")]
    [InlineData("@{1.}")]
    [InlineData("@{1 + @a.b @c}")]
    [InlineData("@{1 + 2")]
    public void Parse_BrokenMath_IsInvalid(string expression)
    {
        BindingExpressions.Parse(expression).Form.ShouldBe(BindingForm.Invalid);
    }

    [Theory]
    [InlineData("@{1}")]
    [InlineData("@{ 1.5 / @rate.value }")]
    [InlineData("@{2 * (3 + @a.b) % 4 - 1}")]
    [InlineData("@{((@a.b))}")]
    [InlineData("@{@f(@a.b, \"x\") + 1}")]
    public void Parse_Math_FollowsTheGrammar(string expression)
    {
        BindingExpressions.Parse(expression).Form.ShouldBe(BindingForm.Math);
    }

    [Fact]
    public void Parse_TenThousandNestedCalls_ExceedsMaxLength_AndIsInvalid()
    {
        // about 40 KB: longer than MaxLength, so it is refused before the reader starts (the depth limit is tested below)
        var parsed = BindingExpressions.Parse(NestedCalls(10_000));

        parsed.Form.ShouldBe(BindingForm.Invalid);
        parsed.ExceedsLimits.ShouldBeTrue();
        parsed.References.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_NestingDeeperThanTheLimit_IsInvalid()
    {
        var deepest = BindingExpressions.Parse(NestedCalls(BindingExpressions.MaxDepth));
        var deeper = BindingExpressions.Parse(NestedCalls(BindingExpressions.MaxDepth + 1));
        var parentheses = BindingExpressions.Parse($"@{{{new string('(', BindingExpressions.MaxDepth + 1)}1{new string(')', BindingExpressions.MaxDepth + 1)}}}");

        deepest.Form.ShouldBe(BindingForm.Function);
        deepest.References.ShouldHaveSingleItem().Key.ShouldBe("x");
        deeper.Form.ShouldBe(BindingForm.Invalid);
        deeper.ExceedsLimits.ShouldBeTrue();
        parentheses.Form.ShouldBe(BindingForm.Invalid);
        parentheses.ExceedsLimits.ShouldBeTrue();
    }

    [Fact]
    public void Parse_LongerThanTheLimit_IsInvalid()
    {
        var longest = "@" + new string('a', BindingExpressions.MaxLength - 1);

        BindingExpressions.Parse(longest).Form.ShouldBe(BindingForm.Path);
        BindingExpressions.Parse(longest + "b").ShouldSatisfyAllConditions(
            parsed => parsed.Form.ShouldBe(BindingForm.Invalid),
            parsed => parsed.ExceedsLimits.ShouldBeTrue());
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("public", true)]
    [InlineData("secret", true)]
    [InlineData("primaryVault", false)]
    public void Reference_KnowsTheValuesStore(string source, bool isValuesStore)
    {
        new BindingReference("db.host", source).IsValuesStore.ShouldBe(isValuesStore);
    }

    [Fact]
    public void Scanner_FollowsTheResolver()
    {
        var document = JObject.Parse(
            """
            {
              "a": "@db.host",
              "plain": "db.host",
              "list": ["x", "@secret(db.password)"],
              "manual": { "$binding": "manual", "note": "@inside.manual" },
              "typo": { "$binding": "json-e", "$definition": {} },
              "rule": { "$binding": "jsone", "$definition": { "v": "@not.bound" }, "$context": { "p": "@db.password" } },
              "extra": { "$binding": "jlogic", "$definition": 1, "other": true }
            }
            """);

        var found = BindingScanner.Scan(document).ToDictionary(binding => binding.Pointer);

        found.Keys.ShouldBe(["/a", "/list/1", "/manual/note", "/typo", "/rule", "/rule/$definition/v", "/rule/$context/p", "/extra"]);
        found["/typo"].IsValid.ShouldBeFalse();
        found["/rule"].IsValid.ShouldBeTrue();
        found["/extra"].IsValid.ShouldBeFalse();
        found["/rule/$context/p"].EnvelopePointer.ShouldBe("/rule");
        found["/rule/$context/p"].InDefinition.ShouldBeFalse();
        found["/a"].EnvelopePointer.ShouldBeNull();
    }

    [Fact]
    public void Scanner_AnAtStringOfTheProgram_IsAPossibleRead()
    {
        // the resolver binds the program's result again, and JSON-e passes a plain string through
        var document = JObject.Parse("""{"conn": {"$binding": "jsone", "$definition": {"password": "@db.password"}}}""");

        var found = BindingScanner.Scan(document).ToDictionary(binding => binding.Pointer);

        var password = found["/conn/$definition/password"];
        password.InDefinition.ShouldBeTrue();
        password.IsValid.ShouldBeTrue();
        password.EnvelopePointer.ShouldBe("/conn");
        password.Parsed.References.ShouldHaveSingleItem().ShouldBe(new BindingReference("db.password", "default"));
        found["/conn"].InDefinition.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"tier": "@plan.tier"}""", true)]
    [InlineData("\"@plan.context\"", true)]
    [InlineData("5", false)]
    [InlineData("true", false)]
    [InlineData("null", false)]
    [InlineData("[]", false)]
    [InlineData("\"plain\"", false)]
    public void Scanner_AContextThatCannotResolveToAnObject_IsNotValid(string context, bool isValid)
    {
        var document = JObject.Parse("""{"rule": {"$binding": "jlogic", "$definition": 1, "$context": """ + context + "}}");

        BindingScanner.Scan(document).First(binding => binding.Pointer == "/rule").IsValid.ShouldBe(isValid);
    }

    // @f(@f(...@x...)): statements nested that deep, the innermost a path
    private static string NestedCalls(int statements)
    {
        return "@" + string.Concat(Enumerable.Repeat("f(@", statements - 1)) + "x" + new string(')', statements - 1);
    }
}
