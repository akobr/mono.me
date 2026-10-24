using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NJsonSchema;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Checks a document against a combined schema with NJsonSchema, as the API checks a write
// (CosmosConfigurationSchemaService.ValidateAgainstSchemaAsync), and reports violations the same way: "{path}: {kind}".
// External references are refused instead of resolved.
public static class SchemaPreviewer
{
    public const string ExternalReferencesReason =
        "External references not previewed: the combined schema refers to a URL or a file, which the MCP host does not fetch or read. The API checks the write when it is made.";

    public const string UnreadableReason =
        "The combined schema could not be read here, so nothing was previewed. The API checks the write when it is made.";

    // A document path for the schema, so that a relative $ref is taken as a file reference (and refused) as well.
    private const string DocumentPath = "/combined-schema.json";

    public static async Task<SchemaPreview> PreviewAsync(string annotationKey, JObject document, JObject schema, CancellationToken cancellationToken)
    {
        JsonSchema parsed;

        try
        {
            parsed = await JsonSchema.FromJsonAsync(schema.ToString(Formatting.None), DocumentPath, LocalReferenceResolver.Factory, cancellationToken);
        }
        catch (Exception exception) when (IsExternalReference(exception))
        {
            return SchemaPreview.NotPreviewed(annotationKey, ExternalReferencesReason);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SchemaPreview.NotPreviewed(annotationKey, UnreadableReason);
        }

        var errors = parsed.Validate(document.ToString(Formatting.None));

        return new SchemaPreview
        {
            AnnotationKey = annotationKey,
            IsPreviewed = true,
            IsValid = errors.Count == 0,
            Errors = errors.Select(error => $"{error.Path}: {error.Kind}").ToList(),
        };
    }

    private static bool IsExternalReference(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception is ExternalSchemaReferenceException)
            {
                return true;
            }
        }

        return false;
    }
}
