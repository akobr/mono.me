using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// The OpenAPI generator inlines every enum and gives it its first value as the default. Inline, NSwag names an enum
/// after each <c>Scopes</c> list of <c>MachineAccess</c> and <c>MachineAccessCreate</c>, and Orval and Zod give every
/// item <c>AnnotationRead</c> as its default, although a scope is only ever granted on purpose. The items reference a
/// shared <c>MachineScope</c> schema instead, with all its values and without a default, as
/// <see cref="MachineCredentialKindDocumentFilter"/> does for <c>EnabledCredentialKinds</c>.
/// </summary>
public class MachineScopeDocumentFilter : IDocumentFilter
{
    public const string SchemaName = nameof(MachineScope);

    private const string ScopesProperty = nameof(MachineAccess.Scopes);

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
                || !schema.Properties.TryGetValue(ScopesProperty, out var scopes)
                || scopes.Items is null)
            {
                continue;
            }

            schemas.TryAdd(SchemaName, new OpenApiSchema
            {
                Type = "string",
                Enum = Enum.GetNames<MachineScope>().Select(IOpenApiAny (value) => new OpenApiString(value)).ToList(),
            });

            scopes.Items = new OpenApiSchema
            {
                Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = SchemaName },
            };
        }
    }
}
