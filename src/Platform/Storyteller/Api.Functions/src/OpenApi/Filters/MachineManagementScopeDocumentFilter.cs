using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// The OpenAPI generator inlines every enum and gives it its first value as the default. Inline, NSwag names an enum
/// after each <c>ManagementScopes</c> list of <c>MachineAccess</c> and <c>MachineAccessCreate</c>, and Orval and Zod
/// give every item <c>ConfigurationTransfer</c> as its default, although a management scope is only ever granted on
/// purpose. The items reference a shared <c>MachineManagementScope</c> schema instead, without a default, as
/// <see cref="MachineCredentialKindDocumentFilter"/> does for <c>EnabledCredentialKinds</c>.
/// </summary>
public class MachineManagementScopeDocumentFilter : IDocumentFilter
{
    public const string SchemaName = nameof(MachineManagementScope);

    private const string ManagementScopesProperty = nameof(MachineAccess.ManagementScopes);

    private static readonly string[] MachineSchemas = [nameof(MachineAccess), nameof(MachineAccessCreate)];

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;

        if (schemas is null)
        {
            return;
        }

        foreach (var name in MachineSchemas)
        {
            if (!schemas.TryGetValue(name, out var schema)
                || schema.Properties is null
                || !schema.Properties.TryGetValue(ManagementScopesProperty, out var managementScopes)
                || managementScopes.Items is null)
            {
                continue;
            }

            schemas.TryAdd(SchemaName, new OpenApiSchema
            {
                Type = "string",
                Enum = Enum.GetNames<MachineManagementScope>().Select(IOpenApiAny (value) => new OpenApiString(value)).ToList(),
            });

            managementScopes.Items = new OpenApiSchema
            {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = SchemaName },
            };
        }
    }
}
