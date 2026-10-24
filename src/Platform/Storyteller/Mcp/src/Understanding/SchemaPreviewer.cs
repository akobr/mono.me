using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NJsonSchema;
using NJsonSchema.Validation;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Checks a document against a combined schema with NJsonSchema, as the API checks a write
// (CosmosConfigurationSchemaService.ValidateAgainstSchemaAsync), and reports violations the same way: "{path}: {kind}".
// External references are refused instead of resolved. The check is synchronous and does not observe cancellation; a
// 'pattern' is matched with the host's default regular expression timeout, and a pattern that runs out of it ends the
// preview.
public static class SchemaPreviewer
{
    public const string ExternalReferencesReason =
        "External references not previewed: the combined schema refers to a URL or a file, which the MCP host does not fetch or read. The API checks the write when it is made.";

    public const string UnreadableReason =
        "The combined schema could not be read here, so nothing was previewed. The API checks the write when it is made.";

    public const string PatternTimeoutReason =
        "A pattern of the combined schema took too long to check here, so nothing was previewed. The API checks the write when it is made.";

    // A document path for the schema, so that a relative $ref is taken as a file reference (and refused) as well.
    private const string DocumentPath = "/combined-schema.json";

    public static Task<SchemaPreview> PreviewAsync(string annotationKey, JObject document, JObject schema, CancellationToken cancellationToken)
    {
        return PreviewAsync(annotationKey, document, schema, static (parsed, json) => parsed.Validate(json), cancellationToken);
    }

    // The tests replace the check: the default match timeout that makes a pattern time out is read once per process,
    // before its first regular expression.
    internal static async Task<SchemaPreview> PreviewAsync(
        string annotationKey,
        JObject document,
        JObject schema,
        Func<JsonSchema, string, ICollection<ValidationError>> validate,
        CancellationToken cancellationToken)
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

        // the validation itself cannot be cancelled, so a call cancelled by now does not start it
        cancellationToken.ThrowIfCancellationRequested();

        ICollection<ValidationError> errors;

        try
        {
            errors = validate(parsed, document.ToString(Formatting.None));
        }
        catch (RegexMatchTimeoutException)
        {
            return SchemaPreview.NotPreviewed(annotationKey, PatternTimeoutReason);
        }

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
