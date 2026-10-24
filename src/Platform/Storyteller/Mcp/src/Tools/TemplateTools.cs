using System.ComponentModel;
using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

[McpServerToolType]
public sealed class TemplateTools(Sdk.ITemplatesApiClient templates)
{
    [McpServerTool(Name = "list_templates", Title = "List templates", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the configuration templates of a view without their content: AnnotationType (the type code), Version and "
        + "Modified. " + ManagementText.TemplatePosition + " Needs the Reader role.")]
    public async Task<CallToolResult> ListTemplates(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        return ToolResults.Json(await templates.GetTemplatesAsync(organization, project, view, cancellationToken));
    }

    [McpServerTool(Name = "get_template", Title = "Get a template", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the configuration template of one annotation type in a view (Content, Version, Modified), or a stored "
        + "version of it with version; 404 when the type has no template. " + ManagementText.TemplatePosition
        + " Needs the Reader role.")]
    public async Task<CallToolResult> GetTemplate(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("A stored version (list_template_versions); omit it for the current template.")] int? version = null,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);

        return ToolResults.Json(version is { } number
            ? await templates.GetTemplateVersionAsync(organization, project, view, type, number, cancellationToken)
            : await templates.GetTemplateAsync(organization, project, view, type, cancellationToken));
    }

    [McpServerTool(Name = "set_template", Title = "Merge into a template", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Creates the template of an annotation type, or merges content into it like a configuration write: "
        + ManagementText.Merge + ". The result is a new version; earlier ones are kept. replace_template makes the "
        + "template equal a given document. " + ManagementText.TemplatePosition + " Needs the Contributor role.")]
    public async Task<CallToolResult> SetTemplate(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ManagementText.Content)] JsonElement content,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);
        var document = ToolArguments.ToObject(content, nameof(content));
        return ToolResults.Json(await templates.SetTemplateAsync(organization, project, view, type, document, cancellationToken));
    }

    [McpServerTool(Name = "replace_template", Title = "Replace a template", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Makes the template of an annotation type equal content. It reads the template and sends a JSON Patch: every "
        + "top-level property it changes or removes is first tested against the stored value, new ones are added, so a "
        + "concurrent change of such a property fails with 412 PatchTestFailed: read again and retry. Without a template "
        + "yet, content becomes its first version. Earlier versions are kept. " + ManagementText.TemplatePosition
        + " Needs the Contributor role. Returns the template and Changed.")]
    public async Task<CallToolResult> ReplaceTemplate(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ManagementText.DesiredContent)] JsonElement content,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);
        var desired = DocumentWrites.ReadDesired(content);
        Sdk.ConfigurationTemplate? current;

        try
        {
            current = await templates.GetTemplateAsync(organization, project, view, type, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            current = null;
        }

        if (current is null)
        {
            // a merge into no template is the document itself (PATCH would answer 404)
            return desired.HasValues
                ? DocumentWrites.Written(await templates.SetTemplateAsync(organization, project, view, type, desired, cancellationToken))
                : DocumentWrites.Unchanged(new JObject { ["AnnotationType"] = type, ["Content"] = new JObject() });
        }

        var stored = ToolResults.ToToken(current.Content) as JObject ?? new JObject();
        var patch = JsonPatchBuilder.Create(stored, desired);

        return patch.Count == 0
            ? DocumentWrites.Unchanged((JObject)ToolResults.ToToken(current).DeepClone())
            : DocumentWrites.Written(await templates.PatchTemplateAsync(organization, project, view, type, patch, cancellationToken));
    }

    [McpServerTool(Name = "patch_template", Title = "Patch a template", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Applies RFC 6902 operations to the template of an annotation type (the paths address its Content). A test "
        + "operation is the only guard against a concurrent change; a failed test answers 412 PatchTestFailed, and a "
        + "type without a template answers 404 (use set_template). " + ManagementText.TemplatePosition + " Needs the "
        + "Contributor role.")]
    public async Task<CallToolResult> PatchTemplate(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ManagementText.Operations)] JsonElement operations,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);
        var patch = DocumentWrites.ReadPatch(operations);
        return ToolResults.Json(await templates.PatchTemplateAsync(organization, project, view, type, patch, cancellationToken));
    }

    [McpServerTool(Name = "delete_template", Title = "Delete a template", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Deletes the template of an annotation type in a view: its values leave the effective configuration of every "
        + "annotation of the type. Its versions are kept until they expire, and a template created later continues the "
        + "numbering. Answers 404 when the type has no template. Needs the Contributor role.")]
    public async Task<CallToolResult> DeleteTemplate(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);
        await templates.DeleteTemplateAsync(organization, project, view, type, cancellationToken);
        return ToolResults.Json(new { AnnotationType = type, Deleted = true });
    }

    [McpServerTool(Name = "list_template_versions", Title = "List template versions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the versions of the template of an annotation type: Version, Modified (who wrote it and when) and "
        + "ExpirationTime; versions other than the current one expire 365 days after their write. Needs the Reader role.")]
    public async Task<CallToolResult> ListTemplateVersions(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);
        return ToolResults.Json(await templates.GetTemplateVersionsAsync(organization, project, view, type, cancellationToken));
    }

    [McpServerTool(Name = "diff_template", Title = "Diff template versions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Compares two stored versions of the template of an annotation type, as hunks of lines with Stats: version "
        + "against versionFrom (default the version before it; version 0 is an empty document). Needs the Reader role.")]
    public async Task<CallToolResult> DiffTemplate(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ManagementText.TemplateType)] string annotationType,
        [Description(ManagementText.DiffVersion)] int version,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ToolText.VersionFrom)] int? versionFrom = null,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);

        return ToolResults.Json(versionFrom is { } from
            ? await templates.GetTemplateVersionDiffCustomAsync(organization, project, view, type, version, from, null, cancellationToken)
            : await templates.GetTemplateVersionDiffAsync(organization, project, view, type, version, null, cancellationToken));
    }
}
