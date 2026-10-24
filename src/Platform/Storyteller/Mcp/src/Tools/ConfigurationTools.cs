using System.ComponentModel;
using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

[McpServerToolType]
public sealed class ConfigurationTools(Sdk.IConfigurationsApiClient configurations, Sdk.IAnnotationsApiClient annotations)
{
    private const string NeedsAnnotation =
        "The annotation must exist (set_annotation or create_annotations creates it); a deleted one is refused.";

    [McpServerTool(Name = "list_configurations", Title = "List configurations", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the configurations stored in a view, without their documents: AnnotationKey, AnnotationType, Version, "
        + "Modified, Hash and HasContent. annotationType and keyPrefix narrow the list. A value inherited from an "
        + "ancestor is stored, and listed, on the ancestor. " + ManagementText.Paged + " Needs the Reader role.")]
    public async Task<CallToolResult> ListConfigurations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Only this annotation type code: rst, unt, sbt, usg, cnt, exe or uxe.")] string? annotationType = null,
        [Description("Only annotation keys that start with this text, for example exe.northwind.")] string? keyPrefix = null,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.ParseOptional(annotationType);
        var prefix = string.IsNullOrWhiteSpace(keyPrefix) ? null : keyPrefix.Trim();
        var page = await ApiPageReader.ReadAsync(continuationToken, limit, async token =>
        {
            var response = await configurations.GetConfigurationsAsync(organization, project, view, type, prefix, token, cancellationToken);
            IReadOnlyList<Sdk.ConfigurationSummary> items = response.Configurations?.ToList() ?? [];
            return (items, response.ContinuationToken);
        });

        return ToolResults.Json(page);
    }

    [McpServerTool(Name = "get_configuration", Title = "Get a configuration", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Reads the configuration of an annotation in one of three forms. \"effective\" (default): the document the "
        + "annotation gets, its ancestors, the view templates of their types and its own document merged, bindings not "
        + "evaluated; its Version is the version of the own document. \"own\": the document stored on this annotation "
        + "alone, the one the write tools change and the pointers of patch_configuration address (Version 0 and "
        + "IsStored false when nothing is stored). \"version\": a stored version of the own document (list_configuration_versions). "
        + "explain_configuration shows where each effective value comes from. Needs the Reader role.")]
    public async Task<CallToolResult> GetConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("effective (default), own or version.")] string form = "effective",
        [Description("The stored version, for form \"version\" only.")] int? version = null,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var kind = form?.Trim().ToLowerInvariant();

        if (kind is not ("effective" or "own" or "version"))
        {
            throw new ToolInputException($"'form' is effective, own or version ('{form}').");
        }

        if ((kind == "version") != version.HasValue)
        {
            throw new ToolInputException("'version' goes with form \"version\", and form \"version\" needs it.");
        }

        switch (kind)
        {
            case "own":
                var own = await StoredConfigurations.GetOwnAsync(configurations, organization, project, view, annotationKey, cancellationToken);
                return ToolResults.Json(new
                {
                    AnnotationKey = annotationKey,
                    own.Version,
                    IsStored = own.Content is not null,
                    own.Content,
                });

            case "version":
                return ToolResults.Json(await configurations.GetConfigurationVersionAsync(organization, project, view, annotationKey, version.GetValueOrDefault(), cancellationToken));

            default:
                return ToolResults.Json(await configurations.GetConfigurationAsync(organization, project, view, annotationKey, cancellationToken));
        }
    }

    [McpServerTool(Name = "set_configuration", Title = "Merge into a configuration", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Merges content into the annotation's own stored document and stores the result as a new version: "
        + ManagementText.Merge + ". A merge cannot drop a property or shorten an array without $remove or $patch; "
        + "replace_configuration makes the stored document equal a given one. " + ManagementText.OwnDocument + " "
        + NeedsAnnotation + " The result is checked against the combined schema unless force is true. Needs the "
        + "Contributor role. Returns the stored own document and its Version.")]
    public async Task<CallToolResult> SetConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ManagementText.Content)] JsonElement content,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.Force)] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var document = ToolArguments.ToObject(content, nameof(content));
        await AnnotationGuards.EnsureAnnotationExistsAsync(annotations, organization, project, view, annotationKey, cancellationToken);

        return ToolResults.Json(await configurations.SetConfigurationAsync(organization, project, view, annotationKey, Force(force), document, cancellationToken));
    }

    [McpServerTool(Name = "replace_configuration", Title = "Replace a configuration", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Makes the annotation's own stored document equal content. It reads the stored document and sends a JSON Patch: "
        + "every top-level property it changes or removes is first tested against the stored value, new ones are added. "
        + "A concurrent change of such a property fails with 412 PatchTestFailed: read again and retry. When nothing is "
        + "stored yet, content becomes the first version. Earlier versions are kept (revert_configuration). "
        + ManagementText.OwnDocument + " " + NeedsAnnotation + " The result is checked against the combined schema "
        + "unless force is true. Needs the Contributor role. Returns the stored own document, its Version and Changed.")]
    public async Task<CallToolResult> ReplaceConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ManagementText.DesiredContent)] JsonElement content,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.Force)] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var desired = DocumentWrites.ReadDesired(content);
        await AnnotationGuards.EnsureAnnotationExistsAsync(annotations, organization, project, view, annotationKey, cancellationToken);

        return await ReplaceAsync(organization, project, view, annotationKey, desired, force, cancellationToken);
    }

    [McpServerTool(Name = "revert_configuration", Title = "Revert a configuration to a version", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Makes the annotation's own stored document equal a previous version (list_configuration_versions), the way "
        + "replace_configuration does: the result is a new version and the history is kept. It also brings back a "
        + "document removed by delete_configuration. " + NeedsAnnotation + " The result is checked against the combined "
        + "schema unless force is true. Needs the Contributor role. Returns the stored own document, its Version and "
        + "Changed.")]
    public async Task<CallToolResult> RevertConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.Version)] int version,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.Force)] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        await AnnotationGuards.EnsureAnnotationExistsAsync(annotations, organization, project, view, annotationKey, cancellationToken);

        var previous = await configurations.GetConfigurationVersionAsync(organization, project, view, annotationKey, version, cancellationToken);
        var desired = ToolResults.ToToken(previous.Content) as JObject ?? new JObject();
        DocumentWrites.EnsureNoDirectives(desired, $"Version {version}");

        return await ReplaceAsync(organization, project, view, annotationKey, desired, force, cancellationToken);
    }

    [McpServerTool(Name = "patch_configuration", Title = "Patch a configuration", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Applies RFC 6902 operations to the annotation's own stored document (read it with get_configuration form "
        + "\"own\"; the paths address it). A test operation is the only guard against a concurrent change: there is no "
        + "version check, and a failed test answers 412 PatchTestFailed. Without a stored configuration it answers 404; "
        + "set_configuration writes a first document. " + NeedsAnnotation + " The result is checked against the combined schema "
        + "unless force is true. Needs the Contributor role. Returns the stored own document and its Version.")]
    public async Task<CallToolResult> PatchConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ManagementText.Operations)] JsonElement operations,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.Force)] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var patch = DocumentWrites.ReadPatch(operations);
        await AnnotationGuards.EnsureAnnotationExistsAsync(annotations, organization, project, view, annotationKey, cancellationToken);

        return ToolResults.Json(await configurations.PatchConfigurationAsync(organization, project, view, annotationKey, Force(force), patch, cancellationToken));
    }

    [McpServerTool(Name = "delete_configuration", Title = "Delete a configuration", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Clears the own stored document of an annotation. The annotation stays, its effective document keeps only "
        + "inherited and template values, and descendants lose what they inherited from it. The cleared content stays "
        + "in the versions (revert_configuration brings it back). Answers 404 when nothing is stored. Needs the "
        + "Contributor role.")]
    public async Task<CallToolResult> DeleteConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        await configurations.DeleteConfigurationAsync(organization, project, view, annotationKey, cancellationToken);
        return ToolResults.Json(new { AnnotationKey = annotationKey, Deleted = true });
    }

    [McpServerTool(Name = "list_configuration_versions", Title = "List configuration versions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the versions of the own stored document of an annotation: Version, Modified (who wrote it and when) and "
        + "ExpirationTime; versions other than the current one expire 365 days after their write. Needs the Reader role.")]
    public async Task<CallToolResult> ListConfigurationVersions(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        return ToolResults.Json(await configurations.GetConfigurationVersionsAsync(organization, project, view, annotationKey, cancellationToken));
    }

    [McpServerTool(Name = "diff_configuration", Title = "Diff a configuration", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Compares configurations, as hunks of lines with Stats. With version: two stored versions of the own document, "
        + "version against versionFrom (default the version before it; version 0 is an empty document). With viewTo: "
        + "the effective documents of the same key in this view and in viewTo. Needs the Reader role.")]
    public async Task<CallToolResult> DiffConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.DiffVersion)] int? version = null,
        [Description(ToolText.VersionFrom)] int? versionFrom = null,
        [Description("Another view to compare the effective document with, instead of versions.")] string? viewTo = null,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var otherView = string.IsNullOrWhiteSpace(viewTo) ? null : viewTo.Trim();

        if (version.HasValue == (otherView is not null) || (versionFrom.HasValue && !version.HasValue))
        {
            throw new ToolInputException("Pass either version (with an optional versionFrom) or viewTo.");
        }

        if (otherView is not null)
        {
            return ToolResults.Json(await configurations.GetConfigurationViewDiffAsync(organization, project, view, annotationKey, otherView, null, cancellationToken));
        }

        return ToolResults.Json(versionFrom.HasValue
            ? await configurations.GetConfigurationVersionDiffCustomAsync(organization, project, view, annotationKey, version.GetValueOrDefault(), versionFrom.Value, null, cancellationToken)
            : await configurations.GetConfigurationVersionDiffAsync(organization, project, view, annotationKey, version.GetValueOrDefault(), null, cancellationToken));
    }

    // The force query is left out unless it is asked for.
    private static bool? Force(bool force)
    {
        return force ? true : null;
    }

    // A patch from the stored own document to the desired one. Nothing stored yet: the desired document is the first
    // version, because a merge into nothing is the document itself (PATCH would answer 404).
    private async Task<CallToolResult> ReplaceAsync(string organization, string project, string view, string key, JObject desired, bool force, CancellationToken cancellationToken)
    {
        var stored = await StoredConfigurations.GetOwnAsync(configurations, organization, project, view, key, cancellationToken);

        if (stored.Content is null)
        {
            return desired.HasValues
                ? DocumentWrites.Written(await configurations.SetConfigurationAsync(organization, project, view, key, Force(force), desired, cancellationToken))
                : DocumentWrites.Unchanged(new JObject { ["AnnotationKey"] = key, ["Version"] = stored.Version, ["Content"] = new JObject() });
        }

        var patch = JsonPatchBuilder.Create(stored.Content, desired);

        return patch.Count == 0
            ? DocumentWrites.Unchanged(new JObject { ["AnnotationKey"] = key, ["Version"] = stored.Version, ["Content"] = stored.Content })
            : DocumentWrites.Written(await configurations.PatchConfigurationAsync(organization, project, view, key, Force(force), patch, cancellationToken));
    }
}
