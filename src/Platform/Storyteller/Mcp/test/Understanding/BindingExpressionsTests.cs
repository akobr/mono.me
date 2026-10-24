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

        found.Keys.ShouldBe(["/a", "/list/1", "/manual/note", "/typo", "/rule", "/rule/$context/p", "/extra"]);
        found["/typo"].IsValid.ShouldBeFalse();
        found["/rule"].IsValid.ShouldBeTrue();
        found["/extra"].IsValid.ShouldBeFalse();
        found["/rule/$context/p"].EnvelopePointer.ShouldBe("/rule");
        found["/a"].EnvelopePointer.ShouldBeNull();
    }
}
