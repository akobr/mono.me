using System.ComponentModel;
using System.Text.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

[McpServerToolType]
public sealed class SchemaTools(Sdk.ISchemasApiClient schemas)
{
    private const string Layers =
        "A write of a configuration is checked against the combined schema of its annotation: the type schema, the "
        + "descendant-type schemas of its ancestors and its annotation schema, deep-merged in that order, from the same "
        + "view only.";

    [McpServerTool(Name = "list_schemas", Title = "List schemas", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists every schema document of a view without its content: Kind (Type, Annotation or DescendantType), "
        + "AnnotationType, AnnotationKey, Version and Modified. " + Layers + " Needs the Reader role.")]
    public async Task<CallToolResult> ListSchemas(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.Json(await schemas.GetConfigurationSchemasAsync(organization, project, view, cancellationToken));
    }

    [McpServerTool(Name = "get_schema", Title = "Get a schema", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns one stored schema document (a JSON Schema) with its Version and Modified, or a stored version of it "
        + "with version. " + SchemaTarget.Levels + ". get_combined_schema returns the schema a write is checked against. "
        + "Needs the Reader role.")]
    public async Task<CallToolResult> GetSchema(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.Level)] string level,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.SchemaType)] string? annotationType = null,
        [Description(ManagementText.SchemaKey)] string? key = null,
        [Description("A stored version (list_schema_versions); omit it for the current schema.")] int? version = null,
        CancellationToken cancellationToken = default)
    {
        var target = SchemaTarget.Parse(level, annotationType, key);
        var (type, annotationKey) = (target.AnnotationType!, target.Key!);

        var schema = (target.Kind, version) switch
        {
            (SchemaTarget.Level.Type, null) => await schemas.GetConfigurationSchemaAsync(organization, project, view, type, cancellationToken),
            (SchemaTarget.Level.Type, { } number) => await schemas.GetSchemaVersionAsync(organization, project, view, type, number, cancellationToken),
            (SchemaTarget.Level.Annotation, null) => await schemas.GetAnnotationSchemaAsync(organization, project, view, annotationKey, cancellationToken),
            (SchemaTarget.Level.Annotation, { } number) => await schemas.GetAnnotationSchemaVersionAsync(organization, project, view, annotationKey, number, cancellationToken),
            (_, null) => await schemas.GetDescendantTypeSchemaAsync(organization, project, view, annotationKey, type, cancellationToken),
            (_, { } number) => await schemas.GetDescendantTypeSchemaVersionAsync(organization, project, view, annotationKey, type, number, cancellationToken),
        };

        return ToolResults.Json(schema);
    }

    [McpServerTool(Name = "set_schema", Title = "Set a schema", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Creates or replaces one schema document with a JSON Schema (no merge with the previous schema; earlier versions "
        + "are kept, list_schema_versions). " + SchemaTarget.Levels + ". Before it is stored, every configuration of the "
        + "view it applies to is checked against the combined schema as it will be: a violation answers 409 with the "
        + "failing annotations and their messages, unless force is true, which stores the schema and leaves those "
        + "configurations as they are (their next write must comply). A descendant-type schema only for a type that can "
        + "descend from key. A $ref must point inside the document (#/...); a reference to a URL or a file is refused, "
        + "because the API would fetch it. " + Layers + " Needs the Contributor role.")]
    public async Task<CallToolResult> SetSchema(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.Level)] string level,
        [Description("The JSON Schema, a JSON object; usually {\"type\":\"object\",\"properties\":{...}}.")] JsonElement schema,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.SchemaType)] string? annotationType = null,
        [Description(ManagementText.SchemaKey)] string? key = null,
        [Description("Store the schema even when stored configurations of the view violate it. Default false.")] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var target = SchemaTarget.Parse(level, annotationType, key);
        var document = ToolArguments.ToObject(schema, nameof(schema));
        EnsureLocalReferences(document);
        var (type, annotationKey) = (target.AnnotationType!, target.Key!);
        bool? forced = force ? true : null;

        var stored = target.Kind switch
        {
            SchemaTarget.Level.Type => await schemas.SetConfigurationSchemaAsync(organization, project, view, type, forced, document, cancellationToken),
            SchemaTarget.Level.Annotation => await schemas.SetAnnotationSchemaAsync(organization, project, view, annotationKey, forced, document, cancellationToken),
            _ => await schemas.SetDescendantTypeSchemaAsync(organization, project, view, annotationKey, type, forced, document, cancellationToken),
        };

        return ToolResults.Json(stored);
    }

    [McpServerTool(Name = "delete_schema", Title = "Delete a schema", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Deletes one schema document of a view; its versions are kept until they expire, and later writes of the "
        + "configurations it applied to are no longer checked against it. " + SchemaTarget.Levels + ". Answers 404 when "
        + "there is no such schema. Needs the Contributor role.")]
    public async Task<CallToolResult> DeleteSchema(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.Level)] string level,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.SchemaType)] string? annotationType = null,
        [Description(ManagementText.SchemaKey)] string? key = null,
        CancellationToken cancellationToken = default)
    {
        var target = SchemaTarget.Parse(level, annotationType, key);
        var (type, annotationKey) = (target.AnnotationType!, target.Key!);

        switch (target.Kind)
        {
            case SchemaTarget.Level.Type:
                await schemas.DeleteConfigurationSchemaAsync(organization, project, view, type, cancellationToken);
                break;
            case SchemaTarget.Level.Annotation:
                await schemas.DeleteAnnotationSchemaAsync(organization, project, view, annotationKey, cancellationToken);
                break;
            default:
                await schemas.DeleteDescendantTypeSchemaAsync(organization, project, view, annotationKey, type, cancellationToken);
                break;
        }

        return ToolResults.Json(new { Level = target.Kind.ToString(), target.AnnotationType, AnnotationKey = target.Key, Deleted = true });
    }

    [McpServerTool(Name = "get_combined_schema", Title = "Get the combined schema of an annotation", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the schema a configuration write of key is checked against: MergedContent, the merged JSON Schema, and "
        + "AppliedSchemas, the stored layers it was merged from. " + Layers + " No layer means no check. The check "
        + "looks at the own stored document only: inherited and template values are invisible to it. Needs the Reader "
        + "role.")]
    public async Task<CallToolResult> GetCombinedSchema(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        return ToolResults.Json(await schemas.GetCombinedConfigurationSchemaAsync(organization, project, view, annotationKey, cancellationToken));
    }

    [McpServerTool(Name = "list_schema_versions", Title = "List schema versions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the versions of one schema document: Version, Modified (who wrote it and when) and ExpirationTime; "
        + "versions other than the current one expire 365 days after their write. " + SchemaTarget.Levels + ". Needs "
        + "the Reader role.")]
    public async Task<CallToolResult> ListSchemaVersions(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.Level)] string level,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.SchemaType)] string? annotationType = null,
        [Description(ManagementText.SchemaKey)] string? key = null,
        CancellationToken cancellationToken = default)
    {
        var target = SchemaTarget.Parse(level, annotationType, key);
        var (type, annotationKey) = (target.AnnotationType!, target.Key!);

        var versions = target.Kind switch
        {
            SchemaTarget.Level.Type => await schemas.GetSchemaVersionsAsync(organization, project, view, type, cancellationToken),
            SchemaTarget.Level.Annotation => await schemas.GetAnnotationSchemaVersionsAsync(organization, project, view, annotationKey, cancellationToken),
            _ => await schemas.GetDescendantTypeSchemaVersionsAsync(organization, project, view, annotationKey, type, cancellationToken),
        };

        return ToolResults.Json(versions);
    }

    [McpServerTool(Name = "diff_schema", Title = "Diff schema versions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Compares two stored versions of one schema document, as hunks of lines with Stats: version against versionFrom "
        + "(default the version before it; version 0 is an empty document). " + SchemaTarget.Levels + ". Needs the "
        + "Reader role.")]
    public async Task<CallToolResult> DiffSchema(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.Level)] string level,
        [Description(ManagementText.DiffVersion)] int version,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.SchemaType)] string? annotationType = null,
        [Description(ManagementText.SchemaKey)] string? key = null,
        [Description(ToolText.VersionFrom)] int? versionFrom = null,
        CancellationToken cancellationToken = default)
    {
        var target = SchemaTarget.Parse(level, annotationType, key);
        var (type, annotationKey) = (target.AnnotationType!, target.Key!);

        var diff = (target.Kind, versionFrom) switch
        {
            (SchemaTarget.Level.Type, null) => await schemas.GetSchemaVersionDiffAsync(organization, project, view, type, version, null, cancellationToken),
            (SchemaTarget.Level.Type, { } from) => await schemas.GetSchemaVersionDiffCustomAsync(organization, project, view, type, version, from, null, cancellationToken),
            (SchemaTarget.Level.Annotation, null) => await schemas.GetAnnotationSchemaVersionDiffAsync(organization, project, view, annotationKey, version, null, cancellationToken),
            (SchemaTarget.Level.Annotation, { } from) => await schemas.GetAnnotationSchemaVersionDiffCustomAsync(organization, project, view, annotationKey, version, from, null, cancellationToken),
            (_, null) => await schemas.GetDescendantTypeSchemaVersionDiffAsync(organization, project, view, annotationKey, type, version, null, cancellationToken),
            (_, { } from) => await schemas.GetDescendantTypeSchemaVersionDiffCustomAsync(organization, project, view, annotationKey, type, version, from, null, cancellationToken),
        };

        return ToolResults.Json(diff);
    }

    // The API resolves every $ref of a schema it parses, on a write of the schema and of each configuration it checks:
    // a reference to a URL would make it fetch the URL from its own network, one to a file read the file. Only
    // references inside the document are sent. A "$ref" whose value is not a string is a property named "$ref". The name
    // is matched in any case, because Newtonsoft falls back to a case-insensitive match and reads "$REF" as a reference.
    private static void EnsureLocalReferences(JObject schema)
    {
        var external = schema.Descendants()
            .OfType<JProperty>()
            .FirstOrDefault(property => string.Equals(property.Name, "$ref", StringComparison.OrdinalIgnoreCase)
                && property.Value is JValue { Type: JTokenType.String } value
                && !((string?)value ?? string.Empty).StartsWith('#'));

        if (external is not null)
        {
            throw new ToolInputException(
                $"The schema refers to '{(string?)external.Value}' at {external.Path}: only references inside the document "
                + "(\"$ref\": \"#/...\") are accepted, because the API would fetch an external reference from its own network.");
        }
    }
}
