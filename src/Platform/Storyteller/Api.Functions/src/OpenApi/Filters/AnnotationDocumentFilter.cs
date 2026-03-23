using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
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
