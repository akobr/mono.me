using _42.Platform.Storyteller.Api.Models;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

public class AnnotationDocumentFilter : IDocumentFilter
{
    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        if (document.Components.Schemas == null)
        {
            return;
        }

        // a merge patch: every member is optional, and null clears it (RFC 7396), except the members a unit always has
        if (document.Components.Schemas.TryGetValue(nameof(AnnotationMergePatch), out var mergePatchSchema))
        {
            mergePatchSchema.Required = new HashSet<string>();

            foreach (var (name, property) in mergePatchSchema.Properties)
            {
                property.Nullable = name is not (nameof(AnnotationMergePatch.UnitType) or nameof(AnnotationMergePatch.UnitDefinition));
            }
        }

        // the PATCH of an annotation takes a JSON Patch (RFC 6902) as well; the attribute declares one media type
        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations.Values))
        {
            if (operation.OperationId == Definitions.RouteIds.Annotations.PatchAnnotation && operation.RequestBody is { } body)
            {
                body.Content[Definitions.ContentTypes.JsonPatch] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "object" } },
                };
            }
        }

        // a restore brings the descendants back unless the client says otherwise; generated clients keep the default
        if (document.Components.Schemas.TryGetValue("AnnotationRestoreRequest", out var restoreSchema)
            && restoreSchema.Properties.TryGetValue("Descendants", out var descendants))
        {
            descendants.Default = new OpenApiBoolean(true);
        }

        if (!document.Components.Schemas.TryGetValue("Annotation", out var annotationSchema))
        {
            return;
        }

        // 1. Add discriminator to the base Annotation schema
        annotationSchema.Discriminator = new OpenApiDiscriminator
        {
            PropertyName = "AnnotationType",
        };

        annotationSchema.Required = new HashSet<string> (["ProjectName", "ViewName", "AnnotationKey", "Name", "AnnotationType"]);

        var derivedTypes = new[]
        {
            "Responsibility",
            "Unit",
            "Subject",
            "Usage",
            "Context",
            "Execution",
            "UnitOfExecution",
        };

        foreach (var typeName in derivedTypes)
        {
            if (document.Components.Schemas.TryGetValue(typeName, out var derivedSchema))
            {
                // 2. Add derived schema to the discriminator mapping
                annotationSchema.Discriminator.Mapping.Add(typeName, $"#/components/schemas/{typeName}");

                // 3. Move unique properties to a separate schema and use allOf
                // We keep the original schema object but replace its content with allOf
                var properties = derivedSchema.Properties;
                var required = derivedSchema.Required;

                // Remove properties that are already in the base Annotation schema to avoid duplication
                foreach (var baseProperty in annotationSchema.Properties.Keys)
                {
                    properties.Remove(baseProperty);
                }

                derivedSchema.Properties = null;
                derivedSchema.Required = new HashSet<string>(properties.Keys);

                derivedSchema.AllOf = new List<OpenApiSchema>
                {
                    new OpenApiSchema
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.Schema,
                            Id = "Annotation",
                        },
                    },
                    new OpenApiSchema
                    {
                        Properties = properties,
                        Required = required,
                        Type = "object",
                    },
                };
            }
        }
    }
}
