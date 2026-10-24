using System.Text.RegularExpressions;

using _42.Platform.Storyteller.Mcp.Understanding;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class SchemaPreviewerTests
{
    private static readonly JObject PortSchema = JObject.Parse(
        """
        {
          "type": "object",
          "properties": { "port": { "$ref": "#/definitions/port" } },
          "required": ["port"],
          "definitions": { "port": { "type": "integer" } }
        }
        """);

    [Fact]
    public async Task AValidDocument_Passes()
    {
        var preview = await SchemaPreviewer.PreviewAsync("sbt.northwind", new JObject { ["port"] = 5432 }, PortSchema, CancellationToken.None);

        preview.IsPreviewed.ShouldBeTrue();
        preview.IsValid.ShouldBe(true);
        preview.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Violations_AreReportedAsTheApiReportsThem()
    {
        var preview = await SchemaPreviewer.PreviewAsync("sbt.northwind", new JObject { ["port"] = "high" }, PortSchema, CancellationToken.None);

        preview.IsValid.ShouldBe(false);
        preview.Errors.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe("#/port: IntegerExpected");
    }

    [Theory]
    [InlineData("https://schemas.example/port.json")]
    [InlineData("http://127.0.0.1:1/port.json#/definitions/port")]
    [InlineData("port.json#/definitions/port")]
    [InlineData("file:///etc/passwd")]
    public async Task ExternalReferences_AreRefused(string reference)
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["port"] = new JObject { ["$ref"] = reference } },
        };

        var preview = await SchemaPreviewer.PreviewAsync("sbt.northwind", new JObject { ["port"] = 1 }, schema, CancellationToken.None);

        preview.IsPreviewed.ShouldBeFalse();
        preview.Reason.ShouldBe(SchemaPreviewer.ExternalReferencesReason);
        preview.IsValid.ShouldBeNull();
    }

    [Fact]
    public async Task ACancelledCall_DoesNotStartTheValidation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var validated = false;

        await Should.ThrowAsync<OperationCanceledException>(() => SchemaPreviewer.PreviewAsync(
            "sbt.northwind",
            new JObject { ["port"] = 1 },
            PortSchema,
            (parsed, json) =>
            {
                validated = true;
                return parsed.Validate(json);
            },
            cancelled.Token));

        validated.ShouldBeFalse();
    }

    [Fact]
    public async Task APatternThatRunsOutOfTheMatchTimeout_IsNotPreviewed()
    {
        // NJsonSchema matches a pattern with the default match timeout, which the host sets; ^(a+)+$ backtracks on this
        // value for hours, so the check stands in for the timeout (the default is read once per process)
        var schema = JObject.Parse("""{"type": "object", "properties": {"a": {"type": "string", "pattern": "^(a+)+$"}}}""");
        var document = new JObject { ["a"] = new string('a', 40) + "!" };

        var preview = await SchemaPreviewer.PreviewAsync(
            "sbt.northwind",
            document,
            schema,
            (_, json) => throw new RegexMatchTimeoutException(json, "^(a+)+$", TimeSpan.FromMilliseconds(200)),
            CancellationToken.None);

        preview.IsPreviewed.ShouldBeFalse();
        preview.Reason.ShouldBe(SchemaPreviewer.PatternTimeoutReason);
        preview.IsValid.ShouldBeNull();
    }
}
