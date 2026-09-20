using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Replaces inline enum definitions for <c>AccountRole</c> (used as dictionary values in
/// <c>AccessPoint.AccessMap</c> and <c>Account.AccessMap</c>) with a <c>$ref</c> to a
/// shared <c>AccountRole</c> schema. Without this filter, NSwag generates anonymous
/// enum types (<c>Anonymous</c>, <c>Anonymous2</c>) because the OpenAPI generator
/// inlines the enum in each <c>additionalProperties</c> definition.
/// </summary>
public class AccountRoleSchemaDocumentFilter : IDocumentFilter
{
    private const string SchemaName = "AccountRole";

    private static readonly string[] EnumValues =
        ["None", "Reader", "Contributor", "ContributorWithSecrets", "Administrator", "Owner"];

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, OpenApiSchema>();

        // 1. Register the shared AccountRole enum schema if not already present.
        if (!document.Components.Schemas.ContainsKey(SchemaName))
        {
            var accountRoleSchema = new OpenApiSchema
            {
                Type = "string",
                Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>(),
            };

            foreach (var value in EnumValues)
            {
                accountRoleSchema.Enum.Add(new Microsoft.OpenApi.Any.OpenApiString(value));
            }

            document.Components.Schemas[SchemaName] = accountRoleSchema;
        }

        // 2. Replace inline enum definitions in AccessMap properties with $ref.
        var schemasToFix = new[] { "AccessPoint", "Account" };

        foreach (var schemaName in schemasToFix)
        {
            if (!document.Components.Schemas.TryGetValue(schemaName, out var schema))
            {
                continue;
            }

            if (schema.Properties == null
                || !schema.Properties.TryGetValue("AccessMap", out var accessMapProp))
            {
                continue;
            }

            // AccessMap is a dictionary — its value type is in AdditionalPropertiesSchema.
            if (accessMapProp.AdditionalProperties != null)
            {
                accessMapProp.AdditionalProperties = new OpenApiSchema
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.Schema,
                        Id = SchemaName,
                    },
                };
            }

        }
    }
}
