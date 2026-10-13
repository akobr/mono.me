using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

// The OpenAPI generator declares the first member as the default of every enum property, also of a nullable one,
// where a missing value means "unset" or "keep" (KeyValueWrite.Protection, KeyValuePolicyUpdate.DefaultSecretProtection,
// KeyValueBulkRequest.Protection). The generated clients would then send that member for an omitted property: the C#
// client initializes it, the Zod schemas apply it. A nullable enum has no default, so the filter removes it.
public class NullableEnumDefaultDocumentFilter : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        if (document.Components?.Schemas is null)
        {
            return;
        }

        foreach (var schema in document.Components.Schemas.Values)
        {
            Strip(schema);
        }
    }

    public static void Strip(OpenApiSchema schema)
    {
        if (schema.Properties is null)
        {
            return;
        }

        foreach (var property in schema.Properties.Values)
        {
            if (property.Nullable && property.Enum is { Count: > 0 } && property.Default is not null)
            {
                property.Default = null;
            }

            if (property.Items is { Nullable: true, Enum: { Count: > 0 }, Default: not null })
            {
                property.Items.Default = null;
            }
        }
    }
}
