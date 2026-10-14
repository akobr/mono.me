using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// The OpenAPI generator gives every enum property its first value as the default. A null
/// <c>CredentialKind</c> of <c>MachineAccessCreate</c> and <c>MachineAuthenticationPolicy</c>
/// means "the project default" (and "the first enabled kind"), so the generated SDKs must not
/// send <c>ApiKey</c> on their own; the default is removed. The items of
/// <c>EnabledCredentialKinds</c> reference a shared <c>MachineCredentialKind</c> schema, so
/// NSwag names the enum after the kind instead of after the list.
/// </summary>
public class MachineCredentialKindDocumentFilter : IDocumentFilter
{
    private const string SchemaName = "MachineCredentialKind";
    private const string PolicySchema = "MachineAuthenticationPolicy";
    private const string CreateSchema = "MachineAccessCreate";
    private const string KindProperty = "CredentialKind";
    private const string EnabledKindsProperty = "EnabledCredentialKinds";

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;

        if (schemas is null)
        {
            return;
        }

        foreach (var name in new[] { PolicySchema, CreateSchema })
        {
            if (schemas.TryGetValue(name, out var schema)
                && schema.Properties?.TryGetValue(KindProperty, out var kind) == true)
            {
                kind.Default = null;
            }
        }

        if (!schemas.TryGetValue(PolicySchema, out var policy)
            || policy.Properties?.TryGetValue(EnabledKindsProperty, out var enabledKinds) != true
            || enabledKinds.Items is null)
        {
            return;
        }

        if (!schemas.ContainsKey(SchemaName))
        {
            schemas[SchemaName] = new OpenApiSchema
            {
                Type = "string",
                Enum = Enum.GetNames<MachineCredentialKind>().Select(IOpenApiAny (value) => new OpenApiString(value)).ToList(),
            };
        }

        enabledKinds.Items = new OpenApiSchema
        {
            Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = SchemaName },
        };
    }
}
