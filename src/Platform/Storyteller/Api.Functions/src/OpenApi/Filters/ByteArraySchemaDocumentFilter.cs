using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

// The OpenAPI generator describes a byte[] property as a string with the format binary, while the API writes it into
// the JSON body as a base64 string (System.Text.Json), for example Pkcs12 of CertificateRenewalResult. Orval types a
// binary string as a Blob and its Zod schema refuses the string the API sends, so the properties of the JSON schemas
// become strings with the format byte (base64); NSwag reads both formats into byte[]. The binary bodies of the routes
// (the zip archives of the view transfer) are not schemas of components and stay binary.
public class ByteArraySchemaDocumentFilter : IDocumentFilter
{
    public const string BinaryFormat = "binary";

    public const string Base64Format = "byte";

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        if (document.Components?.Schemas is null)
        {
            return;
        }

        foreach (var schema in document.Components.Schemas.Values)
        {
            Rewrite(schema);
        }
    }

    public static void Rewrite(OpenApiSchema schema)
    {
        if (schema.Properties is null)
        {
            return;
        }

        foreach (var property in schema.Properties.Values)
        {
            if (IsBinaryString(property))
            {
                property.Format = Base64Format;
            }

            if (property.Items is { } items && IsBinaryString(items))
            {
                items.Format = Base64Format;
            }
        }
    }

    private static bool IsBinaryString(OpenApiSchema schema)
    {
        return string.Equals(schema.Type, "string", StringComparison.Ordinal)
            && string.Equals(schema.Format, BinaryFormat, StringComparison.Ordinal);
    }
}
