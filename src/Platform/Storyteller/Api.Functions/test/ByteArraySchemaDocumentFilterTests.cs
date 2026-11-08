using _42.Platform.Storyteller.Api.OpenApi.Filters;

using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class ByteArraySchemaDocumentFilterTests
{
    [Fact]
    public void Apply_DescribesTheBinaryPropertiesAsBase64_AndKeepsTheOthers()
    {
        var bytes = new OpenApiSchema { Type = "string", Format = "binary" };
        var list = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string", Format = "binary" } };
        var date = new OpenApiSchema { Type = "string", Format = "date-time" };
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, OpenApiSchema>
                {
                    ["CertificateRenewalResult"] = new()
                    {
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["Pkcs12"] = bytes,
                            ["Chunks"] = list,
                            ["LastRenewalAt"] = date,
                        },
                    },
                },
            },
        };

        new ByteArraySchemaDocumentFilter().Apply(null!, document);

        bytes.Format.ShouldBe("byte");
        list.Items.Format.ShouldBe("byte");
        date.Format.ShouldBe("date-time");
    }

    // The API writes a byte[] as a base64 string, so the SDKs must read a string: Orval types format byte as a string
    // (binary would be a Blob), NSwag reads it into byte[]. The zip bodies of the view transfer stay binary.
    [Fact]
    public async Task RenderedDocument_DescribesThePkcs12OfARenewalAsBase64_AndTheArchivesAsBinary()
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        var pkcs12 = root.GetProperty("components").GetProperty("schemas").GetProperty("CertificateRenewalResult").GetProperty("properties").GetProperty("Pkcs12");

        pkcs12.GetProperty("type").GetString().ShouldBe("string");
        pkcs12.GetProperty("format").GetString().ShouldBe("byte");

        var archiveFormats = root.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .SelectMany(operation => ZipSchemas(operation.Value))
            .Select(schema => schema.GetProperty("format").GetString())
            .ToList();

        archiveFormats.ShouldNotBeEmpty();
        archiveFormats.ShouldAllBe(format => format == "binary");
    }

    private static IEnumerable<System.Text.Json.JsonElement> ZipSchemas(System.Text.Json.JsonElement operation)
    {
        var contents = new List<System.Text.Json.JsonElement>();

        if (operation.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return contents;
        }

        if (operation.TryGetProperty("requestBody", out var body) && body.TryGetProperty("content", out var bodyContent))
        {
            contents.Add(bodyContent);
        }

        if (operation.TryGetProperty("responses", out var responses))
        {
            foreach (var response in responses.EnumerateObject())
            {
                if (response.Value.TryGetProperty("content", out var responseContent))
                {
                    contents.Add(responseContent);
                }
            }
        }

        return contents
            .Where(content => content.TryGetProperty("application/zip", out _))
            .Select(content => content.GetProperty("application/zip").GetProperty("schema"))
            .ToList();
    }
}
