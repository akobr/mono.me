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
}
