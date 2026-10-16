using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// The OpenAPI generator inlines every enum and gives it its first value as the default. The include filter of the
/// export and import of a view is a list of <c>ViewArchiveContent</c> on the export's body, on both jobs and in the
/// import's query: inline, NSwag names an enum after each list (<c>Include</c>, <c>Include2</c>, <c>Include3</c>,
/// <c>Anonymous</c>), and every item claims <c>Annotations</c> as its default, although no include means all four
/// kinds. The items reference a shared <c>ViewArchiveContent</c> schema instead, without a default. The import's
/// <c>existing</c> query parameter and the job's <c>Existing</c> reference a shared <c>ViewImportExisting</c> schema,
/// <c>Overwrite</c> by default, so both SDKs have one type for each option.
/// </summary>
public class ViewTransferSchemaDocumentFilter : IDocumentFilter
{
    public const string ContentSchema = nameof(ViewArchiveContent);
    public const string ExistingSchema = nameof(ViewImportExisting);

    private const string IncludeProperty = nameof(ViewImport.Include);
    private const string ExistingProperty = nameof(ViewImport.Existing);

    private static readonly string[] IncludeSchemas = [nameof(ViewExportCreate), nameof(ViewExport), nameof(ViewImport)];

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;

        if (schemas is null || !IncludeSchemas.Any(schemas.ContainsKey))
        {
            return;
        }

        schemas.TryAdd(ContentSchema, EnumSchema<ViewArchiveContent>(defaultValue: null));
        schemas.TryAdd(ExistingSchema, EnumSchema<ViewImportExisting>(ViewImportExisting.Overwrite));

        foreach (var name in IncludeSchemas)
        {
            if (schemas.TryGetValue(name, out var schema)
                && schema.Properties?.TryGetValue(IncludeProperty, out var include) == true
                && include.Items is not null)
            {
                include.Items = Reference(ContentSchema);
            }
        }

        if (schemas.TryGetValue(nameof(ViewImport), out var import)
            && import.Properties?.ContainsKey(ExistingProperty) == true)
        {
            import.Properties[ExistingProperty] = Reference(ExistingSchema);
        }

        var parameters = (document.Paths?.Values ?? Enumerable.Empty<OpenApiPathItem>())
            .SelectMany(path => path.Operations.Values)
            .Where(operation => operation.OperationId == Definitions.RouteIds.Transfers.StartViewImport)
            .SelectMany(operation => operation.Parameters ?? Enumerable.Empty<OpenApiParameter>())
            .Where(parameter => parameter.In == ParameterLocation.Query);

        foreach (var parameter in parameters)
        {
            if (parameter.Name == Definitions.Parameters.Include && parameter.Schema?.Items is not null)
            {
                parameter.Schema.Items = Reference(ContentSchema);
            }
            else if (parameter.Name == Definitions.Parameters.Existing && parameter.Schema is not null)
            {
                parameter.Schema = Reference(ExistingSchema);
            }
        }
    }

    private static OpenApiSchema EnumSchema<TEnum>(TEnum? defaultValue)
        where TEnum : struct, Enum
    {
        return new OpenApiSchema
        {
            Type = "string",
            Enum = Enum.GetNames<TEnum>().Select(IOpenApiAny (value) => new OpenApiString(value)).ToList(),
            Default = defaultValue is { } value ? new OpenApiString(value.ToString()) : null,
        };
    }

    private static OpenApiSchema Reference(string schema)
    {
        return new OpenApiSchema
        {
            Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = schema },
        };
    }
}
