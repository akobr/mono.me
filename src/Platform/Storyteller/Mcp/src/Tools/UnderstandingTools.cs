using System.ComponentModel;
using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Understanding;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Search, explanation and placement of configuration. The analysis is in the Understanding classes; these tools read the
// API and pass the results in. None of them reads a secret: bindings are reported as stored and key/value pairs are read
// without their values.
[McpServerToolType]
public sealed class UnderstandingTools(
    Sdk.IConfigurationsApiClient configurations,
    Sdk.IAnnotationsApiClient annotations,
    Sdk.IKeyValuesApiClient keyValues,
    Sdk.IAccessApiClient access,
    Sdk.ISchemasApiClient schemas,
    Sdk.ITemplatesApiClient templates)
{
    public const int MaxConcurrentReads = 4;

    private const string SearchDescription =
        "Searches the configurations of a view and returns the matches. Key filters (annotationType, keyPrefix, and subject, responsibility, context, "
        + "unit or keyText, each a case-insensitive part of a name in the key) need no document reads. Content filters (property, valueText, bindsKey) "
        + "search the stored own document of each configuration, not the effective one: a value inherited from an ancestor is found on the ancestor, not "
        + "on its descendants. Every given filter must match. Binding expressions are matched and reported as stored; nothing is evaluated and no secret "
        + "is read; a bindsKey match in the $definition of an object binding is only a possible read (Note). Budgets: at most 5 API pages (5,000 "
        + "configurations) and 200 document reads, at most maxResults matches; IsTruncated and TruncatedBy say when a budget stopped the search. Needs "
        + "the Reader role on the project.";

    private const string ExplainDescription =
        "Explains where the values of an annotation's effective configuration come from and who can read them. Values: for every value (or those at and "
        + "under pointer) the merge step that set it, its source (an annotation's configuration or a view template), the node it was merged at and Via, "
        + "the branch it arrived through; for scalar members of objects AlsoSetBy lists the other steps whose document holds a value there (BeforeWinner "
        + "true: merged before the winner, so overridden; false: a later step that sets the same value, which the merge keeps with the earlier step, so it "
        + "is applied too and changing only the winning step does not change the effective value). Objects and "
        + "arrays merge, so they have no AlsoSetBy: their members and items carry their own steps. Array items are attributed by their step only. At "
        + "most 200 values (IsTruncated). Sources: every merged document with the number of values it supplies, Missing and Empty ones included. "
        + "Bindings: the @ expressions and object bindings as stored, with the key/value pairs they read and those pairs' metadata (exists, deleted, "
        + "protection, version; never a value; at most 50 lookups); an @ string in the $definition of an object binding is marked InDefinition, read "
        + "only if the program emits it. Consumers: the platform does not record runtime reads; with includeConsumers the machines whose scopes allow "
        + "configuration reads, by how their annotation restriction reaches the values: ReadsThisDocument (unrestricted, or restricted to the key or an "
        + "ancestor), ReadsThroughDescendants (restricted to a descendant) and ReadsThroughSharedDescendants (restricted to an annotation that shares "
        + "descendants with the key, as rst.billing shares usg.northwind.billing with sbt.northwind); the last two read the values only through such a "
        + "descendant when it exists, where it may override them. Listing machines needs Contributor; otherwise Machines is null with the reason. "
        + "IsStale is true when the effective read differs from the composition. Bindings are not evaluated. Needs the Reader role on the project (the "
        + "composition shows every ancestor's document).";

    private const string PlacementDescription =
        "Suggests where to write a configuration value that belongs to one or more systems, and checks whether it would take effect; writes nothing. "
        + "Each system is named by subject, responsibility, context and unit, which give its most specific annotation (all four: uxe; subject, "
        + "responsibility and context: exe; subject and responsibility: usg; subject and context: cnt; responsibility and unit: unt; one subject: sbt; "
        + "one responsibility: rst). Candidates: with typeDefault first the view template of the systems' type; for one system its own annotation, then "
        + "its ancestors from the most specific; for several systems the most specific annotations they all share, then the other shared ones. For each "
        + "candidate the tool replays every existing system's composition with the value placed there and reports per system and pointer whether it wins "
        + "(Effective) or which step overrides it; candidates effective for every system come first. A pointer stands for one scalar value: through an "
        + "array or at an object or array it is not assessed (Effective null with a Note; give a fragment). For the first three candidates it reports "
        + "whether the annotation exists, is deleted (restore it first) or is missing (create it first), and how many annotations it reaches. With a "
        + "fragment it previews the schema check of the write on the first existing candidate (the API decides at write time; Reason says why a "
        + "preview was skipped, for example a pattern that took too long to check). Needs the Reader role on the project.";

    [McpServerTool(Name = "search_configurations", Title = "Search configurations", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(SearchDescription)]
    public async Task<CallToolResult> SearchConfigurations(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("Only configurations of this annotation type: rst, unt, sbt, usg, cnt, exe or uxe.")] string? annotationType = null,
        [Description("Only annotation keys that start with this text, for example exe.northwind.")] string? keyPrefix = null,
        [Description("A case-insensitive part of the subject name in the key.")] string? subject = null,
        [Description("A case-insensitive part of the responsibility name in the key.")] string? responsibility = null,
        [Description("A case-insensitive part of the context name in the key.")] string? context = null,
        [Description("A case-insensitive part of the unit name in the key.")] string? unit = null,
        [Description("A case-insensitive part of any name in the key.")] string? keyText = null,
        [Description("A property name such as host, or a pointer suffix that starts with '/' such as /database/host: matches the values at such paths of the stored documents (case-insensitive).")] string? property = null,
        [Description("A case-insensitive part of a scalar value (string, number or boolean) of the stored documents.")] string? valueText = null,
        [Description("A Values store key such as db.password: matches the bindings that read it, as @db.password, @secret(db.password), @(db.password, public) or inside an interpolation; a match in the $definition of an object binding is a possible read, marked with a Note.")] string? bindsKey = null,
        [Description("The most matches to return, 1 to 200; default 50.")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var type = TypeCode(annotationType);
        var prefix = string.IsNullOrWhiteSpace(keyPrefix) ? null : keyPrefix.Trim();
        var search = new ConfigurationSearch(subject, responsibility, context, unit, keyText, property, valueText, bindsKey);

        var result = await search.RunAsync(
            (continuationToken, token) => configurations.GetConfigurationsAsync(organization, project, view, type, prefix, continuationToken, token),
            (summary, token) => ReadStoredAsync(organization, project, view, summary, token),
            ConfigurationSearch.ClampMaxResults(maxResults),
            cancellationToken);

        return ToolResults.Json(result);
    }

    [McpServerTool(Name = "get_binding_catalog", Title = "Get the binding catalog", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the binding functions and sources registered on the server, which @ expressions in configurations can use, with signatures, parameters and examples. It never resolves a value. Needs the Reader role on the project.")]
    public async Task<CallToolResult> GetBindingCatalog(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        CancellationToken cancellationToken)
    {
        return ToolResults.Json(await configurations.GetBindingCatalogAsync(organization, project, cancellationToken));
    }

    [McpServerTool(Name = "explain_configuration", Title = "Explain a configuration", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(ExplainDescription)]
    public async Task<CallToolResult> ExplainConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description("An RFC 6901 JSON pointer such as /database/host: explain only the values at and under it. Omit it for the whole document.")] string? pointer = null,
        [Description("Whether to list the machines that can read the values; default true. Listing machines needs the Contributor role.")] bool includeConsumers = true,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key);
        var path = JsonPointer.Parse(pointer, "pointer");
        var keyText = annotationKey.ToString();

        var composition = await configurations.GetConfigurationCompositionAsync(organization, project, view, keyText, cancellationToken);
        var effective = await configurations.GetConfigurationAsync(organization, project, view, keyText, cancellationToken);

        var lookups = ConfigurationExplainer.ValuesStoreKeys(ConfigurationExplainer.BindingsOf(composition, path))
            .Take(ConfigurationExplainer.MaxKeyValueLookups)
            .ToList();
        var metadata = await ReadAllAsync(lookups, (pairKey, token) => ReadKeyValueAsync(organization, project, view, pairKey, token), cancellationToken);

        var consumers = includeConsumers
            ? await ReadConsumersAsync(organization, project, annotationKey, cancellationToken)
            : ConfigurationExplainer.ConsumersUnavailable("Not requested (includeConsumers is false).");

        var explanation = ConfigurationExplainer.Explain(
            composition,
            effective.Hash,
            path,
            lookups.Zip(metadata).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.OrdinalIgnoreCase),
            consumers);

        return ToolResults.Json(explanation);
    }

    [McpServerTool(Name = "suggest_configuration_placement", Title = "Suggest where a configuration value belongs", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(PlacementDescription)]
    public async Task<CallToolResult> SuggestConfigurationPlacement(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description("The systems the value belongs to, 1 to 20, each named by subject, responsibility, context and unit (names: letters, digits, '-' and '_', at most 50 characters).")] PlacementSystem[] systems,
        [Description("The JSON pointers of the values to place, for example [\"/database/host\"]; each pointer is one scalar value outside any array (to place an object or a whole array, give a fragment). Give pointers or fragment, at most 20 values.")] string[]? pointers = null,
        [Description("The values to place as a JSON object, for example {\"database\": {\"host\": \"db.example\"}}; its scalars and arrays are the values (at most 20). Give fragment or pointers; a fragment also previews the schema check.")] JsonElement? fragment = null,
        [Description("Also suggest the view template of the systems' type, a value for every annotation of that type; default false. The systems must be of one type.")] bool typeDefault = false,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        if (systems is not { Length: > 0 } || systems.Length > PlacementAdvisor.MaxSystems)
        {
            throw new ToolInputException($"'systems' needs 1 to {PlacementAdvisor.MaxSystems} systems, for example [{{\"subject\": \"northwind\", \"responsibility\": \"invoicing\"}}].");
        }

        var keys = systems.Select(PlacementAdvisor.KeyOf).ToList();
        var targets = Targets(pointers, fragment);
        var (candidates, notice) = PlacementCandidates.For(keys, typeDefault);

        var systemKeys = keys.Select(item => item.ToString()).Distinct(StringComparer.Ordinal).ToList();
        var compositions = await ReadAllAsync(systemKeys, (systemKey, token) => ReadCompositionAsync(organization, project, view, systemKey, token), cancellationToken);
        var replayed = systemKeys.Zip(compositions, (systemKey, composition) => new PlacementAdvisor.SystemComposition(systemKey, composition)).ToList();

        var ranked = PlacementAdvisor.Rank(candidates.Select(candidate => PlacementAdvisor.Assess(candidate, replayed, targets)));

        for (var index = 0; index < Math.Min(PlacementAdvisor.EvidenceCandidates, ranked.Count); index++)
        {
            ranked[index] = ranked[index] with { Evidence = await EvidenceAsync(organization, project, view, ranked[index], cancellationToken) };
        }

        var preview = targets.Fragment is { } placed
            ? await PreviewAsync(organization, project, view, ranked, placed, cancellationToken)
            : null;

        return ToolResults.Json(new PlacementSuggestion
        {
            Systems = replayed.Select(system => new PlacementSuggestion.SystemKey(system.AnnotationKey, system.Composition is not null)).ToList(),
            Pointers = targets.Pointers,
            Candidates = ranked,
            Notice = notice,
            Today = PlacementAdvisor.Today(replayed, targets),
            SchemaPreview = preview,
            Notes = targets.IgnoresNulls ? [.. PlacementAdvisor.Notes, PlacementAdvisor.IgnoredNullsNote] : PlacementAdvisor.Notes,
        });
    }

    // At most four reads at a time; the results keep the order of the inputs.
    internal static async Task<IReadOnlyList<TResult>> ReadAllAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        Func<TItem, CancellationToken, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        var results = new List<TResult>(items.Count);

        foreach (var batch in items.Chunk(MaxConcurrentReads))
        {
            results.AddRange(await Task.WhenAll(batch.Select(item => read(item, cancellationToken))));
        }

        return results;
    }

    private static string? TypeCode(string? annotationType)
    {
        if (string.IsNullOrWhiteSpace(annotationType))
        {
            return null;
        }

        var code = annotationType.Trim().ToLowerInvariant();
        return AnnotationTypeCodes.ValidCodes.ContainsKey(code)
            ? code
            : throw new ToolInputException($"'annotationType' is not a type code ('{annotationType}'); the codes are rst, unt, sbt, usg, cnt, exe and uxe.");
    }

    private static PlacementTargets Targets(string[]? pointers, JsonElement? fragment)
    {
        var hasFragment = fragment is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };
        var hasPointers = pointers is { Length: > 0 };

        return (hasPointers, hasFragment) switch
        {
            (true, true) => throw new ToolInputException("Give either 'pointers' or 'fragment', not both: a fragment's own leaves are its pointers."),
            (false, false) => throw new ToolInputException("Give the values to place: 'pointers' such as [\"/database/host\"], or a 'fragment' such as {\"database\": {\"host\": \"db.example\"}}."),
            (true, false) => PlacementTargets.FromPointers(pointers!),
            _ => PlacementTargets.FromFragment(ToolArguments.ToObject(fragment!.Value, "fragment")),
        };
    }

    private async Task<JObject?> ReadStoredAsync(string organization, string project, string view, Sdk.ConfigurationSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await configurations.GetConfigurationVersionAsync(organization, project, view, summary.AnnotationKey, checked((int)summary.Version), cancellationToken);
            return ToolResults.ToToken(stored.Content) as JObject;
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            // deleted between the listing and the read
            return null;
        }
    }

    private async Task<KeyValueMetadata> ReadKeyValueAsync(string organization, string project, string view, string key, CancellationToken cancellationToken)
    {
        try
        {
            // reveal: false, so a secret's value never leaves the API
            return KeyValueMetadata.From(await keyValues.GetKeyValueAsync(organization, project, view, key, false, true, cancellationToken));
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            return KeyValueMetadata.Missing;
        }
        catch (Sdk.ApiException exception)
        {
            // one pair that cannot be read does not stop the explanation
            return KeyValueMetadata.Failed(exception.StatusCode, ApiErrors.GetErrorCode(exception));
        }
    }

    private async Task<ConfigurationConsumers> ReadConsumersAsync(string organization, string project, AnnotationKey key, CancellationToken cancellationToken)
    {
        try
        {
            return ConfigurationExplainer.Consumers(key, await access.GetMachineAccessesAsync(organization, project, cancellationToken));
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode is 401 or 403)
        {
            return ConfigurationExplainer.ConsumersUnavailable(
                $"The machines could not be listed (the API answered {exception.StatusCode}): listing machines needs the Contributor role on the project.");
        }
    }

    private async Task<Sdk.ConfigurationComposition?> ReadCompositionAsync(string organization, string project, string view, string key, CancellationToken cancellationToken)
    {
        try
        {
            return await configurations.GetConfigurationCompositionAsync(organization, project, view, key, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            // the annotation does not exist yet (or is deleted, or inactive without a configuration)
            return null;
        }
    }

    private async Task<PlacementEvidence> EvidenceAsync(string organization, string project, string view, PlacementCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Kind == PlacementCandidate.TemplateKind)
        {
            var reach = $"Every {candidate.Level} annotation of the view ({candidate.TemplateTypeCode}.*), also the ones created later.";

            try
            {
                await templates.GetTemplateAsync(organization, project, view, candidate.TemplateTypeCode!, cancellationToken);
                return new PlacementEvidence { State = PlacementEvidence.Exists, Reach = reach };
            }
            catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
            {
                return new PlacementEvidence { State = PlacementEvidence.Missing, Next = "set_template creates the template.", Reach = reach };
            }
        }

        var key = candidate.AnnotationKey!;

        try
        {
            await annotations.GetAnnotationAsync(organization, project, view, key, true, null, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            var deleted = await annotations.FindDeletedAnnotationsAsync(organization, project, view, new Sdk.AnnotationKeysRequest { Keys = [key] }, cancellationToken);

            return (deleted ?? []).Any(annotation => string.Equals(annotation.AnnotationKey, key, StringComparison.Ordinal))
                ? new PlacementEvidence
                {
                    State = PlacementEvidence.Deleted,
                    Next = "The annotation is deleted: restore it first with restore_annotations, then write the configuration; a write on a deleted annotation is refused.",
                    Reach = "None while it is deleted.",
                }
                : new PlacementEvidence
                {
                    State = PlacementEvidence.Missing,
                    Next = "The annotation does not exist: create it with set_annotation or create_annotations (they create missing ancestors), then write the configuration.",
                    Reach = "None until it exists.",
                };
        }

        if (AnnotationKey.Parse(key).Type == AnnotationType.UnitOfExecution)
        {
            return new PlacementEvidence { State = PlacementEvidence.Exists, Descendants = 0, AtLeast = false, Reach = "This annotation only: a unit of execution has no descendants." };
        }

        var descendants = await annotations.GetDescendantsAsync(organization, project, view, key, "all", null, null, null, cancellationToken);
        var count = descendants.Annotations?.Count ?? 0;
        var more = !string.IsNullOrEmpty(descendants.ContinuationToken);

        return new PlacementEvidence
        {
            State = PlacementEvidence.Exists,
            Descendants = count,
            AtLeast = more,
            Reach = $"This annotation and {(more ? "at least " : string.Empty)}{count} active descendant annotation{(count == 1 && !more ? string.Empty : "s")}.",
        };
    }

    // The schema check of a write of the fragment on the first candidate that exists: the API merges the write into the
    // stored own document and checks the result against the combined schema.
    private async Task<SchemaPreview?> PreviewAsync(string organization, string project, string view, IReadOnlyList<PlacementCandidate> candidates, JObject fragment, CancellationToken cancellationToken)
    {
        var candidate = candidates.FirstOrDefault(item => item.Kind == PlacementCandidate.AnnotationKind && item.Evidence?.State == PlacementEvidence.Exists);

        if (candidate?.AnnotationKey is not { } key)
        {
            return null;
        }

        StoredConfiguration stored;

        try
        {
            stored = await StoredConfigurations.GetOwnAsync(configurations, organization, project, view, key, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            stored = new StoredConfiguration(0, null);
        }

        Sdk.CombinedConfigurationSchema combined;

        try
        {
            combined = await schemas.GetCombinedConfigurationSchemaAsync(organization, project, view, key, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            return SchemaPreview.NotPreviewed(key, "No schema applies to this annotation, so the API does not check its configuration.");
        }

        return ToolResults.ToToken(combined.MergedContent) is JObject schema
            ? await SchemaPreviewer.PreviewAsync(key, ConfigurationMerge.Merged(stored.Content, fragment), schema, cancellationToken)
            : SchemaPreview.NotPreviewed(key, "No schema applies to this annotation, so the API does not check its configuration.");
    }
}
