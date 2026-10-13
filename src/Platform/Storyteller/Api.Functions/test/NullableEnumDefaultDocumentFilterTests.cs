using _42.Platform.Storyteller.Api.OpenApi.Filters;

using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class NullableEnumDefaultDocumentFilterTests
{
    [Fact]
    public void Apply_RemovesTheDefaultOfNullableEnums_AndKeepsTheOthers()
    {
        var nullable = Enum(nullable: true);
        var required = Enum(nullable: false);
        var text = new OpenApiSchema { Type = "string", Nullable = true, Default = new OpenApiString("x") };
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, OpenApiSchema>
                {
                    ["KeyValueWrite"] = new()
                    {
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["Protection"] = nullable,
                            ["Level"] = required,
                            ["Value"] = text,
                        },
                    },
                },
            },
        };

        new NullableEnumDefaultDocumentFilter().Apply(null!, document);

        nullable.Default.ShouldBeNull();
        required.Default.ShouldNotBeNull();
        text.Default.ShouldNotBeNull("only enums are touched");
    }

    [Fact]
    public async Task RenderedDocument_HasNoDefaultOnTheNullableLevels()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        schemas.GetProperty("KeyValueWrite").GetProperty("properties").GetProperty("Protection").TryGetProperty("default", out _).ShouldBeFalse();
        schemas.GetProperty("KeyValuePolicyUpdate").GetProperty("properties").GetProperty("DefaultSecretProtection").TryGetProperty("default", out _).ShouldBeFalse();
        schemas.GetProperty("KeyValueBulkRequest").GetProperty("properties").GetProperty("Protection").TryGetProperty("default", out _).ShouldBeFalse();
        schemas.GetProperty("KeyValueDocumentItem").GetProperty("properties").GetProperty("Protection").TryGetProperty("nullable", out _).ShouldBeFalse("a required level stays as it is");
    }

    [Fact]
    public async Task RenderedDocument_DescribesDateOnlyAsADateString()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var dateOnly = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(DateOnlySchemaDocumentFilter.SchemaName);

        dateOnly.GetProperty("type").GetString().ShouldBe("string");
        dateOnly.GetProperty("format").GetString().ShouldBe("date");
        dateOnly.TryGetProperty("properties", out _).ShouldBeFalse();
    }

    private static OpenApiSchema Enum(bool nullable) => new()
    {
        Type = "string",
        Nullable = nullable,
        Enum = [new OpenApiString("Public"), new OpenApiString("Standard")],
        Default = new OpenApiString("Public"),
    };
}
