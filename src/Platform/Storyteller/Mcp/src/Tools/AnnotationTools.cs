using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

[McpServerToolType]
public sealed partial class AnnotationTools(Sdk.IAnnotationsApiClient annotations)
{
    public const int MaxKeys = 100;

    private const string NameRule =
        "A new name (of a responsibility, subject, context or unit that does not exist yet) has 1 to 50 letters, "
        + "numbers, dashes or underscores; existing names are used as they are.";

    private const string DeletedRule =
        "Before a create, the keys and their ancestors are looked up among the deleted annotations; when one is deleted "
        + "the call is refused with the list, because a create would re-create it empty and leave its other descendants "
        + "deleted: restore_annotations restores it, recreateDeleted: true re-creates it.";

    private static readonly string[] DescendantKinds = ["usages", "contexts", "executions", "units", "units-of-execution", "all"];

    private static readonly Dictionary<string, string[]> FindFilters = new(StringComparer.Ordinal)
    {
        [AnnotationTypeCodes.Responsibility] = ["responsibility"],
        [AnnotationTypeCodes.Subject] = ["subject"],
        [AnnotationTypeCodes.Unit] = ["responsibility"],
        [AnnotationTypeCodes.Usage] = ["subject", "responsibility"],
        [AnnotationTypeCodes.Context] = ["subject", "context"],
        [AnnotationTypeCodes.Execution] = ["subject", "responsibility", "context"],
        [AnnotationTypeCodes.UnitOfExecution] = ["subject", "responsibility", "context", "unit"],
    };

    private static readonly string[] ClearableFields =
        ["title", "description", "documentationLink", "labels", "values", "isDisabled", "validFrom", "expiresAt", "timeZone"];

    // How long one name may take to match a find_annotations pattern.
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    [McpServerTool(Name = "list_annotations", Title = "List annotations", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the annotations of a view, a page at a time; only active ones unless includeInactive or includeDeleted "
        + "adds the others. " + ManagementText.CompactItems + " " + ManagementText.Paged + " Needs the Reader role.")]
    public async Task<CallToolResult> ListAnnotations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.IncludeInactive)] bool includeInactive = false,
        [Description(ManagementText.IncludeDeleted)] bool includeDeleted = false,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var page = await ApiPageReader.ReadAsync(continuationToken, limit, async token =>
        {
            var response = await annotations.GetAnnotationsAsync(organization, project, view, token, Flag(includeInactive), Flag(includeDeleted), cancellationToken);
            return (Items(response.Annotations), response.ContinuationToken);
        });

        return Summaries(page);
    }

    [McpServerTool(Name = "get_annotation", Title = "Get an annotation", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns one whole annotation: title, description, documentation link, labels, values, validity window "
        + "(ValidFrom, ExpiresAt, TimeZone), IsDisabled, for units UnitType and UnitDefinition, the keys of its ancestors "
        + "and the Created and Modified stamps, with its Status computed now (Active, Disabled, Scheduled or Expired). "
        + "Inactive annotations are returned too; a deleted one only with includeDeleted. Needs the Reader role.")]
    public async Task<CallToolResult> GetAnnotation(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Also a soft-deleted annotation, with its Deletion. Default false.")] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var annotation = await annotations.GetAnnotationAsync(organization, project, view, annotationKey, true, Flag(includeDeleted), cancellationToken);
        var result = (JObject)ToolResults.ToToken(annotation).DeepClone();
        result["Status"] = AnnotationStatusExtensions.GetStatus(annotation.IsDisabled, annotation.ValidFrom, annotation.ExpiresAt, DateTimeOffset.UtcNow).ToString();
        return ToolResults.Json(result);
    }

    [McpServerTool(Name = "find_annotations", Title = "Find annotations by name", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Finds annotations of one type by the names in their keys. The filters each type takes: rst responsibility; "
        + "sbt subject; unt responsibility; usg subject and responsibility; cnt subject and context; exe subject, "
        + "responsibility and context; uxe subject, responsibility, context and unit. A filter is an exact name, "
        + "compared ignoring case, or a .NET regular expression when it starts with ^ or ends with $ (case-sensitive; "
        + "(?i) ignores case); a pattern that takes too long to match is refused. Every annotation of the type is read "
        + "and its key's names matched page by page, so matches can follow empty pages. Without filters it lists every "
        + "annotation of the type. Only active ones unless includeInactive or includeDeleted adds the others. "
        + ManagementText.CompactItems + " " + ManagementText.Paged + " Needs the Reader role.")]
    public async Task<CallToolResult> FindAnnotations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationType)] string annotationType,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("The responsibility name, or a pattern (^... or ...$).")] string? responsibility = null,
        [Description("The subject name, or a pattern (^... or ...$).")] string? subject = null,
        [Description("The context name, or a pattern (^... or ...$).")] string? context = null,
        [Description("The unit name, or a pattern (^... or ...$); units of execution only.")] string? unit = null,
        [Description(ManagementText.IncludeInactive)] bool includeInactive = false,
        [Description(ManagementText.IncludeDeleted)] bool includeDeleted = false,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCodes.Parse(annotationType);
        var filters = new Dictionary<string, Func<string, bool>?>(StringComparer.Ordinal)
        {
            ["responsibility"] = Filter(responsibility, nameof(responsibility)),
            ["subject"] = Filter(subject, nameof(subject)),
            ["context"] = Filter(context, nameof(context)),
            ["unit"] = Filter(unit, nameof(unit)),
        };

        var allowed = FindFilters[type];
        var refused = filters.Where(filter => filter.Value is not null && !allowed.Contains(filter.Key)).Select(filter => filter.Key).ToList();

        if (refused.Count > 0)
        {
            throw new ToolInputException(
                $"annotationType {type} takes the filters {string.Join(" and ", allowed)}, not {string.Join(" and ", refused)}.");
        }

        var given = filters.Where(filter => filter.Value is not null).Select(filter => (Name: filter.Key, Match: filter.Value!)).ToList();
        var (inactive, deleted) = (Flag(includeInactive), Flag(includeDeleted));

        // The typed routes take name queries but drop them for most types and fail on responsibilities, so the names
        // are matched here. A page is filtered before the host page takes its offset: a re-read of the same API page
        // gives the same matches, so the host cursor stays in place.
        Func<string?, Task<(IReadOnlyList<Sdk.Annotation> Items, string? Next)>> read = type switch
        {
            AnnotationTypeCodes.Responsibility => async token =>
            {
                var response = await annotations.GetResponsibilitiesAsync(organization, project, view, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
            AnnotationTypeCodes.Subject => async token =>
            {
                var response = await annotations.GetSubjectsAsync(organization, project, view, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
            AnnotationTypeCodes.Unit => async token =>
            {
                var response = await annotations.GetUnitsAsync(organization, project, view, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
            AnnotationTypeCodes.Usage => async token =>
            {
                var response = await annotations.GetUsagesAsync(organization, project, view, null, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
            AnnotationTypeCodes.Context => async token =>
            {
                var response = await annotations.GetContextsAsync(organization, project, view, null, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
            AnnotationTypeCodes.Execution => async token =>
            {
                var response = await annotations.GetExecutionsAsync(organization, project, view, null, null, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
            _ => async token =>
            {
                var response = await annotations.GetUnitsOfExecutionAsync(organization, project, view, null, null, null, null, token, inactive, deleted, cancellationToken);
                return (Items(response.Annotations), response.ContinuationToken);
            },
        };

        var page = await ApiPageReader.ReadAsync(continuationToken, limit, given.Count == 0 ? read : async token =>
        {
            var (items, next) = await read(token);
            IReadOnlyList<Sdk.Annotation> matches = items.Where(item => Matches(item, given)).ToList();
            return (matches, next);
        });

        return Summaries(page);
    }

    [McpServerTool(Name = "list_descendants", Title = "List descendants of an annotation", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the descendants of an annotation along the inheritance graph. descendants is usages, contexts, executions, "
        + "units, units-of-execution or all (default): a responsibility has every kind but contexts, a subject every "
        + "kind but units, a usage or a context executions and units of execution, a unit or an execution units of "
        + "execution, a unit of execution none. Only active ones unless includeInactive or includeDeleted adds the "
        + "others. " + ManagementText.CompactItems + " " + ManagementText.Paged + " Needs the Reader role.")]
    public async Task<CallToolResult> ListDescendants(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("usages, contexts, executions, units, units-of-execution or all (default).")] string descendants = "all",
        [Description(ManagementText.IncludeInactive)] bool includeInactive = false,
        [Description(ManagementText.IncludeDeleted)] bool includeDeleted = false,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var kind = descendants?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!DescendantKinds.Contains(kind))
        {
            throw new ToolInputException($"'descendants' is one of {string.Join(", ", DescendantKinds)} ('{descendants}').");
        }

        var page = await ApiPageReader.ReadAsync(continuationToken, limit, async token =>
        {
            var response = await annotations.GetDescendantsAsync(organization, project, view, annotationKey, kind, token, Flag(includeInactive), Flag(includeDeleted), cancellationToken);
            return (Items(response.Annotations), response.ContinuationToken);
        });

        return Summaries(page);
    }

    [McpServerTool(Name = "set_annotation", Title = "Create or update an annotation", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Creates or updates one annotation; the body is built from the key (its type, name and the keys and names of "
        + "its ancestors). On an update the fields left out keep their stored values, clearFields removes fields, and "
        + "values replaces the whole set of values; annotation fields keep no versions and there is no rollback: "
        + "get_annotation_history shows earlier fields, labels and simple values for a manual restore, but only flags a changed "
        + "description, so a replaced description is lost. On a create: " + NameRule + " A unit needs unitType and "
        + "unitDefinition. Missing ancestors are created by the API with the validity window, time zone, disabled flag "
        + "and labels of this annotation (a unit created that way gets UnitType \"event\" and UnitDefinition "
        + "\"unknown\"). " + DeletedRule + " Needs the Contributor role. Returns the stored annotation.")]
    public async Task<CallToolResult> SetAnnotation(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("The title, at most 200 characters.")] string? title = null,
        [Description("The description, at most 10,000 characters.")] string? description = null,
        [Description("An absolute http or https URL of the documentation, at most 2048 characters.")] string? documentationLink = null,
        [Description("The labels; replaces the stored labels.")] string[]? labels = null,
        [Description("The values, a JSON object of names and values; replaces the whole stored set.")] JsonElement? values = null,
        [Description("Whether the annotation is disabled (not in effect).")] bool? isDisabled = null,
        [Description("When it comes into effect: an ISO 8601 date and time with an offset, for example 2026-11-01T00:00:00Z.")] string? validFrom = null,
        [Description("When it stops being in effect: an ISO 8601 date and time with an offset, for example 2027-01-01T00:00:00+01:00.")] string? expiresAt = null,
        [Description("The IANA time zone in which validFrom and expiresAt are shown and entered, for example Europe/Prague.")] string? timeZone = null,
        [Description("Units only, required on create: the kind of the unit, for example time, message, file or event.")] string? unitType = null,
        [Description("Units only, required on create: its definition, for example a CRON expression or a message template.")] string? unitDefinition = null,
        [Description("Fields to remove: title, description, documentationLink, labels, values, isDisabled, validFrom, expiresAt or timeZone.")] string[]? clearFields = null,
        [Description(ManagementText.RecreateDeleted)] bool recreateDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key);
        var keyText = annotationKey.ToString();
        var isUnit = annotationKey.Type == AnnotationType.Unit;

        if (!isUnit && (unitType is not null || unitDefinition is not null))
        {
            throw new ToolInputException($"unitType and unitDefinition belong to units (unt.{{responsibility}}.{{unit}}), not to '{keyText}'.");
        }

        var given = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = title is not null,
            ["description"] = description is not null,
            ["documentationLink"] = documentationLink is not null,
            ["labels"] = labels is not null,
            ["values"] = IsGiven(values),
            ["isDisabled"] = isDisabled is not null,
            ["validFrom"] = validFrom is not null,
            ["expiresAt"] = expiresAt is not null,
            ["timeZone"] = timeZone is not null,
        };
        var cleared = ParseClearFields(clearFields, given);
        var newValues = IsGiven(values) ? ToValues(values.GetValueOrDefault()) : null;
        var newValidFrom = ParseInstant(validFrom, nameof(validFrom));
        var newExpiresAt = ParseInstant(expiresAt, nameof(expiresAt));

        var stored = await TryGetAsync(organization, project, view, keyText, cancellationToken);
        var body = AnnotationBodies.Create(annotationKey, project, view);

        if (stored is not null)
        {
            AnnotationBodies.CopyEditable(stored, body);
        }
        else
        {
            if (isUnit && (string.IsNullOrWhiteSpace(unitType) || string.IsNullOrWhiteSpace(unitDefinition)))
            {
                throw new ToolInputException($"The new unit '{keyText}' needs unitType and unitDefinition.");
            }

            await AnnotationGuards.EnsureNewNamesAsync(annotations, organization, project, view, [annotationKey], cancellationToken);

            if (!recreateDeleted)
            {
                var deleted = await AnnotationGuards.FindDeletedAsync(annotations, organization, project, view, AnnotationGuards.WithAncestors([annotationKey]), cancellationToken);
                AnnotationGuards.EnsureNoneDeleted(deleted);
            }
        }

        body.Title = title ?? body.Title;
        body.Description = description ?? body.Description;
        body.DocumentationLink = documentationLink ?? body.DocumentationLink;
        body.Labels = labels is null ? body.Labels : [.. labels];
        body.Values = newValues ?? body.Values;
        body.IsDisabled = isDisabled ?? body.IsDisabled;
        body.ValidFrom = newValidFrom ?? body.ValidFrom;
        body.ExpiresAt = newExpiresAt ?? body.ExpiresAt;
        body.TimeZone = timeZone ?? body.TimeZone;

        if (body is Sdk.Unit unitBody)
        {
            unitBody.UnitType = unitType ?? unitBody.UnitType;
            unitBody.UnitDefinition = unitDefinition ?? unitBody.UnitDefinition;
        }

        Clear(body, cleared);
        return ToolResults.Json(await annotations.SetAnnotationAsync(organization, project, view, keyText, body, cancellationToken));
    }

    [McpServerTool(Name = "create_annotations", Title = "Create annotations", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(
        "Creates up to 100 annotations, with their missing ancestors, from keys or compact lines; keys that exist are "
        + "skipped. A compact line has no type code: subject (sbt), subject.responsibility (usg), "
        + "subject.responsibility.context (exe) or subject.responsibility.context.unit (uxe); a line that starts with a "
        + "type code is a key. " + NameRule + " A unit created here gets UnitType \"event\" and UnitDefinition "
        + "\"unknown\" (set_annotation changes them). " + DeletedRule + " Needs the Contributor role. Returns the created "
        + "annotations (compact, ancestors included) and Skipped, the requested keys that existed.")]
    public async Task<CallToolResult> CreateAnnotations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("1 to 100 annotation keys or compact lines, for example [\"northwind.invoicing.production\", \"rst.billing\"].")] string[] lines,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(ManagementText.RecreateDeleted)] bool recreateDeleted = false,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Length > MaxKeys)
        {
            throw new ToolInputException($"'lines' takes 1 to {MaxKeys} keys or compact lines.");
        }

        var keys = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(CompactAnnotationLines.ToKey)
            .DistinctBy(key => key.ToString(), StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
        {
            throw new ToolInputException($"'lines' takes 1 to {MaxKeys} keys or compact lines.");
        }

        // a key that exists is skipped by the API, so its names are not new
        await AnnotationGuards.EnsureNewNamesAsync(annotations, organization, project, view, keys, cancellationToken);

        if (!recreateDeleted)
        {
            var deleted = await AnnotationGuards.FindDeletedAsync(annotations, organization, project, view, AnnotationGuards.WithAncestors(keys), cancellationToken);
            AnnotationGuards.EnsureNoneDeleted(deleted);
        }

        var keyTexts = keys.Select(key => key.ToString()).ToList();
        var created = (await annotations.SetAnnotationsSimpleAsync(organization, project, view, keyTexts, cancellationToken))?.ToList() ?? [];
        var createdKeys = created.Select(annotation => annotation.AnnotationKey).ToHashSet(StringComparer.Ordinal);

        return ToolResults.Json(new
        {
            Created = AnnotationSummary.From(created, DateTimeOffset.UtcNow),
            Skipped = keyTexts.Where(key => !createdKeys.Contains(key)).ToList(),
        });
    }

    [McpServerTool(Name = "delete_annotation", Title = "Delete an annotation", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Soft-deletes an annotation with all its descendants (inactive ones too) and hides their configurations. They "
        + "can be restored with restore_annotations until an Administrator purges them or the retention runs out "
        + "(Deletion.PurgeAt, by default 90 days after the delete). A repeated delete returns the existing deletion. "
        + "Needs the Contributor role. Returns the Deletion (DeletionId, PurgeAt, DescendantCount) and DeletedKeys, "
        + "descendants first and the annotation last.")]
    public async Task<CallToolResult> DeleteAnnotation(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        return ToolResults.Json(await annotations.DeleteAnnotationAsync(organization, project, view, annotationKey, cancellationToken));
    }

    [McpServerTool(Name = "list_deleted_annotations", Title = "List deleted annotations", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists deleted annotations, newest deletion first, each with its Deletion: DeletionId, RootKey, DeletedAt, "
        + "DeletedBy, IsRoot, DescendantCount on a root, and PurgeAt (when it is purged automatically). By default only "
        + "the roots of deletions (the keys a delete was asked for); roots false lists every deleted annotation. With "
        + "deletionId it lists every annotation that one delete removed, unless roots is true. "
        + ManagementText.CompactItems + " " + ManagementText.Paged + " Needs the Reader role.")]
    public async Task<CallToolResult> ListDeletedAnnotations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Only the roots of deletions: default true, or false with deletionId; false lists every deleted annotation.")] bool? roots = null,
        [Description("Only what one delete removed: the DeletionId of a Deletion.")] string? deletionId = null,
        [Description(ToolText.Limit)] int? limit = null,
        [Description(ToolText.ContinuationToken)] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var deletion = string.IsNullOrWhiteSpace(deletionId) ? null : deletionId.Trim();

        // The API's default (roots only) applies next to a deletionId too and would leave out what that delete removed
        // below its root, so a deletionId alone lists every annotation of the deletion.
        var rootsOnly = roots ?? deletion is null;
        var page = await ApiPageReader.ReadAsync(continuationToken, limit, async token =>
        {
            var response = await annotations.GetDeletedAnnotationsAsync(organization, project, view, rootsOnly ? null : false, deletion, token, cancellationToken);
            return (Items(response.Annotations), response.ContinuationToken);
        });

        return Summaries(page);
    }

    [McpServerTool(Name = "restore_annotations", Title = "Restore deleted annotations", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Restores up to 100 deleted annotations, ancestors first: deleted ancestors are restored alone and missing ones "
        + "created; with descendants (default true) also everything the key's deletion removed below it, while "
        + "descendants deleted by an earlier, separate delete stay deleted. A key that fails does not stop the others: "
        + "the call can succeed partly, and Failed lists the keys with their error code and message. Needs the "
        + "Contributor role. Returns the restored and created annotations (compact), CreatedKeys and Failed.")]
    public async Task<CallToolResult> RestoreAnnotations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("1 to 100 keys of deleted annotations (list_deleted_annotations).")] string[] keys,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Also restore what each key's deletion removed below it. Default true.")] bool descendants = true,
        CancellationToken cancellationToken = default)
    {
        if (keys is null || keys.Length is 0 or > MaxKeys)
        {
            throw new ToolInputException($"'keys' takes 1 to {MaxKeys} annotation keys.");
        }

        var keyTexts = keys.Select(key => AnnotationKeys.Parse(key, "keys").ToString()).Distinct(StringComparer.Ordinal).ToList();
        var result = await annotations.RestoreAnnotationsAsync(organization, project, view, new Sdk.AnnotationRestoreRequest { Keys = keyTexts, Descendants = descendants }, cancellationToken);

        return ToolResults.Json(new
        {
            Annotations = AnnotationSummary.From(result.Annotations ?? [], DateTimeOffset.UtcNow),
            result.CreatedKeys,
            result.Failed,
        });
    }

    [McpServerTool(Name = "purge_annotation", Title = "Delete an annotation permanently", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Deletes a deleted annotation permanently, with its deleted descendants, their configurations (the content stays "
        + "in the configuration versions until they expire) and their history; it cannot be undone. The annotation must "
        + "be deleted first (delete_annotation): an active key answers 409 AnnotationNotDeleted. Needs the Administrator "
        + "role; machines are refused. Returns PurgedKeys, descendants first.")]
    public async Task<CallToolResult> PurgeAnnotation(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("The key of a deleted annotation (list_deleted_annotations).")] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        return ToolResults.Json(await annotations.PurgeAnnotationAsync(organization, project, view, annotationKey, cancellationToken));
    }

    [McpServerTool(Name = "get_annotation_history", Title = "Get the history of an annotation", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns what happened with an active or deleted annotation and who did it, newest first: created, updated (the "
        + "previous and new values of fields, labels and values; the description is only flagged), deleted, restored, "
        + "re-created, and configuration changes with their version. IsTrimmed says that the oldest entries were "
        + "dropped. A purged or unknown key answers 404. Needs the Reader role.")]
    public async Task<CallToolResult> GetAnnotationHistory(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        return ToolResults.Json(await annotations.GetAnnotationHistoryAsync(organization, project, view, annotationKey, cancellationToken));
    }

    // Optional query flags are left out when they keep the API's default.
    private static bool? Flag(bool value)
    {
        return value ? true : null;
    }

    private static IReadOnlyList<Sdk.Annotation> Items<T>(ICollection<T>? items)
        where T : Sdk.Annotation
    {
        return items?.Cast<Sdk.Annotation>().ToList() ?? [];
    }

    private static CallToolResult Summaries(HostPage.Page<Sdk.Annotation> page)
    {
        return ToolResults.Json(new HostPage.Page<AnnotationSummary>(AnnotationSummary.From(page.Items, DateTimeOffset.UtcNow), page.ContinuationToken));
    }

    // An exact name, compared ignoring case, or a regular expression (^... or ...$) with a match timeout: a pattern that
    // backtracks without end fails the call instead of holding the host. % patterns are refused rather than compared
    // literally, because they read like SQL LIKE.
    private static Func<string, bool>? Filter(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        if (text.StartsWith('%') || text.EndsWith('%'))
        {
            throw new ToolInputException($"'{parameter}' is an exact name or a regular expression that starts with ^ or ends with $; % patterns are not supported.");
        }

        if (!text.StartsWith('^') && !text.EndsWith('$'))
        {
            return name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase);
        }

        Regex pattern;

        try
        {
            pattern = new Regex(text, RegexOptions.None, PatternTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new ToolInputException($"'{parameter}' is not a valid regular expression: {exception.Message}");
        }

        return name =>
        {
            try
            {
                return pattern.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new ToolInputException(
                    $"'{parameter}' took too long to match the name '{name}'; simplify the pattern (nested repetitions such as (a+)+ backtrack without end).");
            }
        };
    }

    private static bool Matches(Sdk.Annotation annotation, IReadOnlyList<(string Name, Func<string, bool> Match)> filters)
    {
        return AnnotationKey.TryParse(annotation.AnnotationKey ?? string.Empty, out var key)
            && filters.All(filter => filter.Match(filter.Name switch
            {
                "responsibility" => key.ResponsibilityName,
                "subject" => key.SubjectName,
                "context" => key.ContextName,
                _ => key.UnitName,
            }));
    }

    private static bool IsGiven(JsonElement? element)
    {
        return element is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
    }

    private static Dictionary<string, object> ToValues(JsonElement element)
    {
        return ToolArguments.ToObject(element, "values").Properties().ToDictionary(property => property.Name, property => (object)property.Value, StringComparer.Ordinal);
    }

    private static HashSet<string> ParseClearFields(string[]? clearFields, Dictionary<string, bool> given)
    {
        var cleared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in clearFields ?? [])
        {
            var name = ClearableFields.FirstOrDefault(candidate => string.Equals(candidate, field?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ToolInputException($"'clearFields' names fields among {string.Join(", ", ClearableFields)} ('{field}').");

            if (given[name])
            {
                throw new ToolInputException($"'{name}' is both given and in clearFields.");
            }

            cleared.Add(name);
        }

        return cleared;
    }

    private static void Clear(Sdk.Annotation body, HashSet<string> cleared)
    {
        foreach (var field in cleared)
        {
            switch (field)
            {
                case "title":
                    body.Title = null;
                    break;
                case "description":
                    body.Description = null;
                    break;
                case "documentationLink":
                    body.DocumentationLink = null;
                    break;
                case "labels":
                    body.Labels = null;
                    break;
                case "values":
                    body.Values = null;
                    break;
                case "isDisabled":
                    body.IsDisabled = null;
                    break;
                case "validFrom":
                    body.ValidFrom = null;
                    break;
                case "expiresAt":
                    body.ExpiresAt = null;
                    break;
                case "timeZone":
                    body.TimeZone = null;
                    break;
            }
        }
    }

    // An instant needs its offset: without one the host's time zone would decide it.
    private static DateTimeOffset? ParseInstant(string? text, string parameter)
    {
        if (text is null)
        {
            return null;
        }

        var value = text.Trim();

        return InstantPattern().IsMatch(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant
            : throw new ToolInputException($"'{parameter}' is not an ISO 8601 date and time with an offset ('{text}'), for example 2026-11-01T00:00:00Z.");
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex InstantPattern();

    private async Task<Sdk.Annotation?> TryGetAsync(string organization, string project, string view, string key, CancellationToken cancellationToken)
    {
        try
        {
            return await annotations.GetAnnotationAsync(organization, project, view, key, true, cancellationToken: cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            return null;
        }
    }
}
