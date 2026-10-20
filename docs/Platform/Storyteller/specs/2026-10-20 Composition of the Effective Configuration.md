# Composition of the Effective Configuration

Linear: [424-36 Add possibility to see how configuration is build](https://linear.app/424fun/issue/424-36/add-possibility-to-see-how-configuration-is-build)

## Problem

The Effective read returns one merged JSON document. It does not say which documents were merged, in which order, or which document set a given value. A user who sees an unexpected `retries: 1` on an execution has to open every ancestor and every type template and replay the merge in their head. The merge is recursive and applies a shared ancestor once per branch, so the replay is easy to get wrong. A value set by a usage can come back from its subject through the context branch.

The issue asks for a view that shows the source of every line of the effective document, like the blame view on GitHub:

- a response model of ordered sub-sections, one per merged document (a configuration or a template), with the content each one brings;
- enough data to rebuild the whole effective document and to see the final source of each property;
- the view in the Storyteller API (`Api.Functions`), in `sform` (`Cli`) and in the admin UI (`ui.admin`), where it could be a second view of the `Effective` tab.

The inputs to this spec are:

- the issue;
- the CodeRabbit coding plan attached to the issue (`2026-10-20 Add possibility to see how configuration is build.md`), used as a second opinion and a source of ideas, not as the decision.

Section 13 lists where this spec differs from that plan.

## Current State

All paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The Effective calculation

- **The read.** `CosmosConfigurationService.GetRawConfigurationAsync(FullKey)` (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs:138-184`) reads the item `{view}.cnf.{annotationKey}`:
  - an item with `AnnotationDeletionId` returns null (the annotation is soft-deleted);
  - no item and an annotation state other than `Active` (`AnnotationItemStore.ReadStateAsync`) returns null;
  - an item with `CalculatedContent` returns the cache;
  - otherwise it builds the graph and calls `CalculateAndCacheConfigurationAsync`.
- **The graph.** `BuildInheritanceGraph(FullKey)` (`:1282-1341`) builds a DAG of `InheritanceGraphNode`. `GetAncestors()` (`Configuring/InheritanceGraphNode.cs:20`) orders the direct parents of one node by the `AnnotationType` enum. Some nodes are shared: on an execution, the subject sits under both the usage and the context; on a unit of execution, the responsibility sits under both the unit and the usage, and the subject under both the usage and the context.

  | Type | Direct parents, in merge order |
  | --- | --- |
  | Unit | rst |
  | Usage | rst, sbt |
  | Context | sbt |
  | Execution | usg, cnt |
  | UnitOfExecution | unt, exe |

- **The calculation.** `CalculateAndCacheConfigurationAsync` (`:930-1042`) runs per node, recursively:
  1. a cached `CalculatedContent` of the node is returned as it is;
  2. it starts from `{}` and merges the **calculated** document of each direct parent, in `GetAncestors()` order;
  3. `TryAutogeneratePropertiesAsync` (`:853-875`) merges the view template of the node's type once (`{view}.gen.{typeCode}` in `{project}.template`, read once per type and calculation);
  4. it merges the node's stored `Content`;
  5. when the result has at least one property, it writes it as `CalculatedContent` with `CalculatedContentHash`, or creates a cache-only item (`Content = {}`, version 0, `Modified` by System) for a node without an item (`:996-1019`). User writes always give version 1 or more, and clearing a configuration deletes its item (`:631`), so version 0 with `{}` marks an item that holds only the cache.

  There is no visited set: a shared node is calculated, and merged, once per branch. Ancestors are not filtered by deletion or state. The method writes, so a read-only feature cannot reuse it.
- **The hash.** `JObjectExtensions.CalculateMurmurHash32Bits` (`Backend.CosmosDb/src/JObjectExtensions.cs:12`) over the Newtonsoft `JsonSettingNames.Unique` serialization, formatted `x8`. It depends on the property order. The read returns no hash for an empty document.
- **The existing hierarchy read.** `GetConfigurationHierarchyViewAsync` (`:226-250`) returns the stored `Content` per visited key. It records a shared node once, omits templates and has no HTTP route.

### The merge

`JsonExtensions.MergeInto` (`Backend.CosmosDb/src/JsonExtensions.cs:64`) is Newtonsoft `JObject.Merge` with `MergeArrayHandling.Union` and `MergeNullValueHandling.Ignore` (`:23`). Per property of the incoming object:

- absent in the target: the incoming value is added, even `null`;
- incoming `null` on an existing property: ignored;
- object into object, array into array: merged recursively;
- arrays: incoming items not already present (deep equality) are appended; existing items and their order stay;
- any other pairing (a scalar, or a different type): the incoming value replaces the existing one, except that a scalar equal to the existing scalar (same token type and value) leaves the existing token in place (`JProperty.SetItem` skips an unchanged `JValue`).

The merge is **not associative**. Merging a parent's finished document is not the same as merging the parent's sources one after the other into the child. Example: the target already holds `a: {x: 1}`, and a parent is composed of a template `a: 5` and its own `a: {y: 2}`.

- The parent's document is `a: {y: 2}` (5 is replaced by the object). Merged into the target: `a: {x: 1, y: 2}`. This is what the Effective read does.
- Replaying the parent's sources flat: `5` replaces `{x: 1}`, then `{y: 2}` replaces `5`, giving `a: {y: 2}`.

A flat list of layers therefore cannot rebuild the document. The grouping by node has to be part of the model.

### API, SDKs and clients

- **API.** `Api.Functions/src/V1/ConfigurationHttp.cs:84-124` (`GetConfiguration`) checks the scopes (`Configuration.Read`, `Configuration.ReadWrite`, `Default.Read`, `Default.ReadWrite`), checks `CheckAccessToAnnotationAsync`, parses the key (`TryParseAnnotationKey`, `:622`), calls the service, maps null through `NotFoundAsync` (`:640`, `AnnotationDeleted` for a soft-deleted annotation), and answers through `OkOrNotModified` with a weak `ConfigurationEntityTags.ForEffective` tag and `Cache-Control: private, no-cache`. `TemplateHttp` checks `CheckAccessToProjectAsync` (`Api.Functions/src/Security/HttpRequestDataExtensions.cs:213`), which also refuses a machine restricted to one annotation (`403 AnnotationRestricted`).
- **Entity tags.** `Api.Functions/src/ConditionalReads/ConfigurationEntityTags.cs`: `W/"<prefix>.<base64url of 16 bytes of SHA-256>"` over length-prefixed inputs; prefixes `e`, `r`, `b`.
- **SDKs.** The OpenAPI document is exported by `Api.Functions/test/OpenApiExportTests.cs` (`STORYTELLER_OPENAPI_EXPORT`). `Sdk.NSwag` generates `ApiSdk.g.cs` with `nswag openapi2csclient` (`MultipleClientsFromFirstTagAndOperationId`). `sdk.typescript` generates the fetch client, Vue Query hooks, Zod schemas and MSW mocks with Orval from the newest `open.api.v*.json`. The newest snapshot on `main` is `open.api.v0.9.32.json`. The version is the file version of `Api.Functions`: `0.9` plus the number of commits that touched `Api.Functions/src`.
- **`sform`.** `Cli/src/Commands/Configuration/ConfigGetCommand.cs` is the `config` command. `config <key>` prints the effective document with `Console.WriteJson`, which writes to the static console and is invisible to the test `RecordingConsole`. Subcommands are listed in its `[Subcommand]` attribute. `Commands/Schemas/SchemaOperations.cs` shows the current error handling: `Console.ValidateAnnotationKey`, a 404 message, then `WriteApiError` for any other `ApiException` but 401.
- **Admin UI.** `src/Platform/ui.admin/src/app/components/configuration/ConfigurationEffective.vue` shows the effective document in a read-only `DocumentEditor` with a JSON/YAML toggle, the binding gutter (`components/editor/setup/bindings.ts`) and the first 12 characters of `Hash`. `useConfiguration` starts the effective, own and combined-schema queries eagerly, on four tabs (Edit, Effective, Versions, Schema). A template save invalidates every configuration query of the view (`TemplateDetail.vue`, `invalidateAllConfigurations`); a configuration save invalidates only that key. `CodeEditor.vue` accepts CodeMirror extensions and exposes `view` and `goToLine`. `utils/configuration-origin.ts` and `AnnotationLineage.vue` compute a flat, deduplicated ancestor order on the client, which is not the merge sequence. The mock Effective handler (`app/mock/configuration-handlers.ts:53`, `effectiveContent`) merges flat: the ancestors' own documents, then the key's own type template, then its own content, with `deepMerge`, which replaces arrays and lets `null` overwrite.
- **Docs.** `docs/Platform/Storyteller/inheritance.md` explains the calculation and the reversal examples (`currency` NOK over USD on an execution, `retries` 1 over 9 on a unit of execution). `templating.md` places the template. `docs/42for.net/platform/configuration.md` gives the merge order as a flat list without templates.

## Proposed Changes

### 1. The composition model (`Abstractions.Annotations`)

A new file `Abstractions.Annotations/src/Model/ConfigurationComposition.cs`, in the style of `ConfigurationSummary.cs` (namespace `_42.Platform.Storyteller`, `public record class` with `required … { get; init; }`, one `//` comment per type and per non-obvious member, Newtonsoft `StringEnumConverter` on the enums):

```csharp
// How the effective configuration of an annotation is composed: every document that is merged, the calculations that
// group the merges, the merges in the order of application, and the step that set each value of the result.
public record class ConfigurationComposition
{
    public required string AnnotationKey { get; init; }

    // The effective document, composed from the sources below.
    public required JObject Content { get; init; }

    // Hash of Content, calculated as the Effective read calculates it; null for an empty document.
    public string? Hash { get; init; }

    // Every document that takes part, once, even when it is applied more than once.
    public required IReadOnlyList<ConfigurationCompositionSource> Sources { get; init; }

    // The calculations, in the order they finish; the target is the last one.
    public required IReadOnlyList<ConfigurationCompositionNode> Nodes { get; init; }

    // The merges of source documents, in the order of application.
    public required IReadOnlyList<ConfigurationCompositionStep> Steps { get; init; }

    // The step that set each value of Content, in document order.
    public required IReadOnlyList<ConfigurationCompositionOrigin> Origins { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationCompositionSourceKind
{
    Configuration = 0,
    Template,
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ConfigurationCompositionSourcePresence
{
    // No stored configuration (no item, or an item that holds only the cache); it is applied as nothing.
    Missing = 0,

    // A stored document without properties.
    Empty,

    Present,
}

public record class ConfigurationCompositionSource
{
    // "configuration:{annotationKey}" or "template:{typeCode}".
    public required string Id { get; init; }

    public required ConfigurationCompositionSourceKind Kind { get; init; }

    // The type of the annotation, or the type the template is for.
    public required AnnotationType AnnotationType { get; init; }

    // The annotation of a configuration; null for a template.
    public string? AnnotationKey { get; init; }

    public required ConfigurationCompositionSourcePresence Presence { get; init; }

    // 0 when Missing.
    public required ulong Version { get; init; }

    // Null when Missing.
    public AuditStamp? Modified { get; init; }

    // The stored document; null when Missing.
    public JObject? Content { get; init; }
}

// One calculation of the inheritance graph. A shared ancestor is calculated once per branch, so it has a node per branch.
public record class ConfigurationCompositionNode
{
    // The position in Nodes.
    public required int Index { get; init; }

    public required string AnnotationKey { get; init; }

    // The node whose calculation merges the result of this one; null for the target.
    public int? MergedInto { get; init; }

    // 0 for the target, 1 for its direct ancestors, and so on.
    public required int Depth { get; init; }
}

public record class ConfigurationCompositionStep
{
    // The order of application, from 1.
    public required int Order { get; init; }

    public required string SourceId { get; init; }

    // The node whose calculation applies the source.
    public required int Node { get; init; }
}

public record class ConfigurationCompositionOrigin
{
    // RFC 6901 JSON Pointer into Content, for example "/database/hosts/0".
    public required string Path { get; init; }

    // The order of the step that set the value.
    public required int Step { get; init; }
}
```

Rules:

1. **Nodes.** The inheritance graph unfolded into a tree: a shared ancestor gets one node per branch. Nodes are listed in the order their calculations finish (post-order), so every node comes after all of its ancestors and the target is last.
   - On an execution (6 nodes): `rst` (via `usg`), `sbt` (via `usg`), `usg`, `sbt` (via `cnt`), `cnt`, `exe`.
   - On a unit of execution (9 nodes): `rst` (via `unt`), `unt`, `rst` (via `usg`), `sbt` (via `usg`), `usg`, `sbt` (via `cnt`), `cnt`, `exe`, `uxe`. The responsibility and the subject, with their templates, are each applied twice.
2. **Steps.** Each node applies at most one template step and exactly one configuration step, in this order. A configuration step exists even when the configuration is `Missing`; it applies nothing, and it keeps the whole lineage visible. A template step exists only when the view has a template for the node's type (`Empty` or `Present`). The steps of a node come after the steps of every node listed before it. On an execution with templates for every type the steps are: 1 rst template, 2 rst configuration, 3 sbt template, 4 sbt configuration, 5 usg template, 6 usg configuration, 7 sbt template, 8 sbt configuration, 9 cnt template, 10 cnt configuration, 11 exe template, 12 exe configuration.
3. **Sources.** Each document is listed once, in the order of its first application. The same source can appear in several steps (the subject and its template twice on an execution). An item with version 0 and `{}` content holds only the cache that an Effective read wrote, so it is `Missing` (version 0, no `Modified`, no `Content`), like no item at all. `Empty` is a stored document without properties (version 1 or more). The composition therefore does not depend on whether an Effective read ran before it.
4. **Replay.** A client rebuilds `Content` by walking `Nodes` in order. For each node: start from `{}`, merge the result of every node whose `MergedInto` is this node (in `Nodes` order), then merge the `Content` of each step of this node (in `Order`, skipping `Missing`). The result of the last node is `Content`. Clients do not have to replay; the server sends `Content`.
5. **Origins.** Every value of `Content` except the root object has exactly one origin, listed in document order (a container before its children; a property counts as its value). The origin is the step whose document put the value where it is, following the merge rules:
   - a value added to an absent property, or replacing another value, takes the origin it had in the incoming document;
   - an incoming scalar equal to the existing one (same token type and value; `1` and `1.0` differ) changes nothing, so the value keeps the step that first set it;
   - an object merged into an object keeps its origin; its properties are attributed one by one;
   - an incoming `null` on an existing property changes nothing;
   - an array merged into an array keeps its origin and the origins of its existing items (duplicates already in the target included); an incoming item deep-equal to an existing item, or to an earlier incoming item, is skipped; appended items take their incoming origins; an array added to an absent property keeps all its items;
   - within one step's document, every value takes that step;
   - within a node's finished document merged into a child, every value keeps the origin it had in the node's calculation.

   On the execution example of `inheritance.md` (subject `currency: NOK, residency: norway`, usage `currency: USD, plan: enterprise`, context `endpoint`), `/currency` comes from the subject's configuration step via the context, `/residency` from the subject's configuration step via the usage (the second application repeats the same value), `/plan` from the usage and `/endpoint` from the context. A step can therefore set no value at all.
6. **Hash.** `Hash` is computed over `Content` exactly as the Effective calculation computes `CalculatedContentHash`, and is null when `Content` has no properties. When the stored sources match the cache, it equals the Effective `Hash`; a client compares the two to notice a stale view (section 8).

### 2. The read-only composition (`Backend.Core`, `Backend.CosmosDb`)

1. **Interface.** `IConfigurationService` (`Backend.Core/src/Configuring/IConfigurationService.cs`) gets, next to `GetConfigurationHierarchyViewAsync`:

   ```csharp
   // How the effective configuration is composed, from the stored documents; null as GetRawConfigurationAsync.
   Task<ConfigurationComposition?> GetConfigurationCompositionAsync(FullKey key);
   ```

   `FakeConfigurationService` (`Backend.Core/test/Transferring/TransferFakes.cs`) implements it with `throw new NotSupportedException()`, like its other members.
2. **Implementation.** `CosmosConfigurationService` becomes `partial` and the new code goes to `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.Composition.cs`.
   - **The root** follows `GetRawConfigurationAsync`: null when the target item has `AnnotationDeletionId`, and null when there is no item and the annotation state is not `Active`. An existing item is composed without a state check, as the Effective read returns it.
   - **The walk** uses `BuildInheritanceGraph(key)` and a private recursive method that mirrors `CalculateAndCacheConfigurationAsync` for each node: merge the finished document of each `GetAncestors()` parent, in order; then the type template; then the stored `Content`. It never reads `CalculatedContent`, never writes, and creates no cache item.
   - **Reads.** Each configuration item and each template is read once per request into a dictionary (by annotation key, by type code), with `TryReadItemAsync`. A shared node reuses its reads; the merges are repeated, as in the Effective calculation. Deleted ancestors are not filtered, as in the Effective calculation. The template's `Version` and `Modified` come from its `GenerateTemplateEntity`.
   - **Presence.** No item, or an item with version 0 whose `Content` has no properties (a cache-only item): `Missing`, with version 0 and neither `Modified` nor `Content`. Another item whose `Content` has no properties: `Empty`. Otherwise `Present`. A template item is `Empty` or `Present`.
   - **Result.** `Content` is the target's finished document, cloned without annotations (section 3). `Hash` uses the same `CalculateMurmurHash32Bits(_jsonSettingsProvider.GetSettings(JsonSettingNames.Unique))` and `x8` format, only when `Content.HasValues`.
3. **No change** to `GetRawConfigurationAsync`, `CalculateAndCacheConfigurationAsync`, the cache or the invalidation. The template step is a single `MergeInto`, so no helper has to be shared between the two walks.

### 3. Origins by annotation (`Backend.CosmosDb`)

The origins are computed by the real merge, not by a second implementation of its rules. Newtonsoft keeps token annotations: since 13.0.2 a clone copies them (`JsonCloneSettings.CopyAnnotations`, on by default), and `Merge` clones every token it takes from the incoming document, while the tokens it keeps (an equal scalar, an existing container or array item) keep theirs. Checks on 13.0.4 confirmed the rules of section 1.5, the nested case and the equal scalar included.

A new `public static class ConfigurationCompositionOrigins` in `Backend.CosmosDb/src/Configuring/ConfigurationCompositionOrigins.cs` (public, like `JsonExtensions`, because the project has no `InternalsVisibleTo`):

- `JObject Mark(JObject content, int step)` returns a deep clone of `content` in which every value token (every token except `JProperty`, whose instances `Merge` recreates) carries the annotation `StepOrigin(step)` (a private `sealed record`). The stored document is never annotated.
- `IReadOnlyList<ConfigurationCompositionOrigin> Collect(JObject content)` walks the value tokens of `content` in document order (the root and the `JProperty` tokens excluded; a property contributes its value) and returns one origin per value token, with RFC 6901 pointers (`~` as `~0`, `/` as `~1`, array indexes as numbers). A value token without an annotation is a defect: it throws `InvalidOperationException`.
- `JObject Strip(JObject content)` returns a clone without annotations (`DeepClone(new JsonCloneSettings { CopyAnnotations = false })`).

The walk of section 2 merges `Mark(source.Content, step.Order)` with the existing `MergeInto` at each step, and merges each parent's finished (annotated) document as it is. After the target, `Collect` gives `Origins` and `Strip` gives `Content`.

### 4. The HTTP read (`Api.Functions`)

1. **Definitions** (`Api.Functions/src/Definitions.cs`):
   - route `Routes.Configuration.V1.ConfigurationComposition` = `Configuration + "/composition"`, that is `v1/{organization}/{project}/{view}/configuration/{key}/composition`;
   - operation id `RouteIds.Configuration.GetConfigurationComposition`;
   - a description of the operation: what the composition is, that a machine restricted to one annotation is refused, and that it is a conditional read (reuse `Descriptions.ConditionalRead`).
2. **Handler.** `ConfigurationHttp` becomes `partial`; `Api.Functions/src/V1/ConfigurationHttp.Composition.cs` adds `GetConfigurationComposition`. The function name equals the operation id. GET, tag `Configurations`, the same path parameters and security attributes as `GetConfiguration`. The constructor does not change. In this order:
   1. `request.CheckScope(Scopes.Configuration.Read, Scopes.Configuration.Write, Scopes.Default.Read, Scopes.Default.Write)`;
   2. `await request.CheckAccessToProjectAsync(_access, organization, project)`, the gate of `TemplateHttp` (section 4.3);
   3. `TryParseAnnotationKey`, then `FullKey.Create(annotationKey, organization, project, view)`;
   4. `_configuration.GetConfigurationCompositionAsync(fullKey)`; null goes through `NotFoundAsync(fullKey)`, so a soft-deleted annotation answers `404 AnnotationDeleted`;
   5. `request.MarkNoCache()` and `request.OkOrNotModified(composition, ConfigurationEntityTags.ForComposition(fullKey, composition))`.

   Bindings are not evaluated: the composition works on the effective document, like `GetConfiguration`.
3. **Authorization.** The whole project, not the annotation. The composition shows ancestor documents in full, including values the target overrides, and the templates, which `TemplateHttp` serves only to callers of the whole project. A machine restricted to an annotation already gets its effective configuration, which is what inheritance is for; it does not need the documents behind it. Users are not restricted to annotations, so for them the gate is the same `Reader` role as `GetConfiguration`.
4. **OpenAPI.** `200` with `ConfigurationComposition` and the `EntityTagResponseHeader`, `304` without a body with the header, `400`, `404` (`ResponseConfigurationNotFound`), `401` (the four scopes) and `500`, with the same attribute shapes as `GetConfiguration`. `ForbiddenResponseDocumentFilter` adds the `403`.
5. **Entity tag.** `ConfigurationEntityTags.ForComposition(FullKey key, ConfigurationComposition composition)`: prefix `c` (`CompositionPrefix`), weak, the same digest and length-prefixed inputs: the prefix, `key.ToString()`, and the composition serialized with Newtonsoft (`Formatting.None`). The serialization covers every source's identity, version, presence and content, the nodes, steps and origins, so a change in any source changes the tag even when the effective document does not (a value moving from one ancestor to another). `ForEffective` is not reused. The class comment lists the new prefix.

### 5. API tests (`Api.Functions/test`)

- **`ConfigurationCompositionHttpTests`** (new), with a `DispatchProxy` or `Recording<IConfigurationService>()` double as the existing tests do:
  - a composition answers `200` with the model, a weak tag `^W/"c\.[A-Za-z0-9_-]{22}"$` and `Cache-Control: private, no-cache`;
  - a matching `If-None-Match` answers `304` with the tag;
  - the tag changes when a source's version changes and `Content` and `Hash` do not;
  - an invalid key answers `400` without calling the service;
  - null from the service answers `404`; a deleted annotation answers `404` with `AnnotationDeleted`.
- **`MachineRestrictionHttpTests.Handlers.cs`**: the handler joins `ProjectWideHandlers`, so a restricted machine gets `403 AnnotationRestricted`, even for its own annotation, before the service is called.
- **`ConfigurationConditionalReadTests.Document_ConfigurationReads_DeclareTheNotModifiedAndTheTag`**: a new `InlineData` with the operation id.
- **`RouteTableTests`**: the new route is registered and unique.
- **`OpenApiDocumentTests`**: the property names of `ConfigurationComposition`, `ConfigurationCompositionSource`, `ConfigurationCompositionNode`, `ConfigurationCompositionStep` and `ConfigurationCompositionOrigin`; the inline enums of `ConfigurationCompositionSource` (`Kind` is `Configuration`, `Template`; `Presence` is `Missing`, `Empty`, `Present`; `AnnotationType` has the seven type names); `ConfigurationCompositionNode.MergedInto` is a nullable integer. The extension writes enum properties inline, as for `ViewImportItem.Kind`, so there is no component schema for these enums and no document filter is added.
- Run in Debug (`DEV_AUTH`) and in Release.

### 6. OpenAPI snapshot and SDKs

The documented workflow, without hand edits of generated files:

1. Export the document after the API commit through `OpenApiExportTests` (`STORYTELLER_OPENAPI_EXPORT`), name it `open.api.v<info.version without the 4th part>.json`, put it in `Sdk.NSwag/` and in `sdk.typescript/`, and remove `sdk.typescript/open.api.v0.9.32.json`. `Sdk.NSwag` keeps its history.
2. `nswag openapi2csclient` with the command of `Sdk.NSwag/README.md` (from PowerShell), and update the input file name in that README.
3. `npm ci`, `npm run generate`, then `typecheck`, `lint`, `test` and `build` in `sdk.typescript/src`. A second `generate` must leave `generated/` unchanged (CI checks it).
4. The operation stays in the `Configurations` tag, so `IConfigurationsApiClient.GetConfigurationCompositionAsync` appears in the existing client, and no change is needed in `ServicesCollectionExtensions.cs`, `src/msw.ts` or `SdkHttpClientConfigurationTests`. NSwag names the inline enums `ConfigurationCompositionSourceKind`, `ConfigurationCompositionSourcePresence` and `ConfigurationCompositionSourceAnnotationType`. The TypeScript names are `getConfigurationComposition`, `useGetConfigurationComposition`, `getGetConfigurationCompositionQueryKey`, `GetConfigurationCompositionResponse` (Zod) and `getGetConfigurationCompositionMockHandler`.
5. **Date-like strings.** The NSwag client reads JSON with Newtonsoft's default `DateParseHandling.DateTime`, so an ISO date string inside `Content` becomes a date token and is written back in another form (local offset, other fractions). `Sdk.NSwag/src/ApiSdk.partial.cs` implements `static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)` for `ConfigurationsApiClient` with `settings.DateParseHandling = DateParseHandling.None`. Typed `DateTimeOffset` properties (`AuditStamp.At`) still parse. This also keeps the strings of `config <key>` as stored.
6. TypeScript SDK tests: a row for `getConfigurationComposition` in the Zod table of `test/msw.test.ts`, and in `test/helpers.test.ts` an assertion that `getGetConfigurationCompositionQueryKey(…)` starts with the configuration's path, so `invalidateConfiguration` covers it.

The model is flat on purpose: no type refers to itself, so the OpenAPI extension, NSwag and the Orval Zod output need no recursive schema.

### 7. `sform config composition`

1. **Command.** `Cli/src/Commands/Configuration/ConfigCompositionCommand.cs`, `public class ConfigCompositionCommand : BaseContextCommand`, constructor `(IExtendedConsole console, ICommandContext context, IConfigurationsApiClient configurationApi, IFileSystem fileSystem)`:
   - `[Command(CommandNames.COMPOSITION, Description = "Show how the effective configuration is composed: the merged documents in order, and the step that set each line.")]`; `CommandNames.COMPOSITION = "composition"` next to `DIFF` and `VERSIONS`;
   - `[Argument(0)] AnnotationKey`, `[Option("-e|--export", CommandOptionType.SingleValue)] string? ExportFilePath`;
   - registered in the `[Subcommand]` list of `ConfigGetCommand`, so the full path is `sform storyteller config composition <key>` (`story config composition`), and a `story-config-composition` profile in `Properties/launchSettings.json`;
   - `Console.ValidateAnnotationKey(AnnotationKey)` (`Output/ExtendedConsoleExtensions.cs`), then `GetConfigurationCompositionAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key)`;
   - a 404 prints `The configuration for '{key}' has not been found.` and returns `ERROR_WRONG_INPUT`; any other `ApiException` except 401 goes through the public `Console.WriteApiError(exception)` (`Output/KeyValueConsoleExtensions.cs`), which prints the server's message and error code and returns `WARNING_UNAUTHORIZED_ACCESS` for a 403, so a restricted machine sees why; 401 is left to `Program`;
   - with `--export`, the whole response is written by `WriteJsonToFile`.
2. **Output.** `Cli/src/Output/CompositionConsoleExtensions.cs`, `WriteComposition(this IExtendedConsole @this, ConfigurationComposition composition)`, all through `IExtendedConsole`, so `RecordingConsole` sees it:
   - `WriteHeader("Composition of the configuration for '{key}'")`;
   - a table through `WriteTable` with the columns `Step`, `Applied at`, `Source`, `Version`, `Presence`, `Values`: the step order; the node's annotation key indented by two spaces per `Depth`; `template <TypeName>` or `configuration`; `v<n>` or `—`; `present`, `empty` or `missing`; the number of origins that point to the step;
   - `WriteHeader("Effective configuration, by step")` and the blame listing: one line per line of the indented JSON, `{step,3} │ {text}`, the step column blank for the root braces.
3. **Lines.** A pure `CompositionLines.Of(JObject content, IEnumerable<ConfigurationCompositionOrigin> origins)` in the same folder returns `(string Text, string Pointer, int? Step)` per line. It writes the JSON itself, so the mapping is exact: two spaces per level, `"name": value`, scalars by `JToken.ToString(Formatting.None)`, `{}` and `[]` for empty containers. A line that opens or closes a container, or holds a scalar, takes the origin of that value, looked up by pointer; the root braces have none. The SDK hands `Content` over as `object`, a `JObject` at runtime.
4. **Tests.** `Cli/test/Commands/ConfigCompositionCommandTests.cs` with `AnnotationCommandFixture`, `RecordingConsole` and a strict `Mock<IConfigurationsApiClient>`: the exact SDK call, the table rows, the blame lines, the 404 message and exit code, a 403 with `AnnotationRestricted` (exit code `WARNING_UNAUTHORIZED_ACCESS` and the `Error code` line), an invalid key, the export payload on a `MockFileSystem`, and option parsing. `Cli/test/Output/CompositionLinesTests.cs`: leaves, nested containers, arrays, empty containers, escaped names, the root, and a date-like string kept as it is.

### 8. Admin UI: Composition in the Effective tab

Paths are relative to `src/Platform/ui.admin/src/`.

1. **Toggle.** `ConfigurationEffective.vue` gets a `Document | Composition` toggle at the top of the tab (a radiogroup like `FormatToggle`). The choice is the route query `show=composition`, written with `router.replace({ query: { ...route.query, show } })` and omitted for `Document`, so a link can open the composition directly. (`mode` is taken: the parent configurations list reads it through `useAnnotationFilter` as `grid`/`tree`.) The tab links keep only `q`, so switching tabs drops it. `Document` stays the default and keeps the current content (`DocumentEditor`, JSON/YAML, bindings, hash).
2. **Query.** `composables/useConfigurationComposition.ts` wraps `useGetConfigurationComposition` with `enabled` only while the Composition view is shown (and the key and view are set), `staleTime: 0` and `refetchOnWindowFocus: true`. It is not added to `useConfiguration`, which runs eagerly on four tabs (Edit, Effective, Versions, Schema). No new invalidation is needed: the query key sits under the configuration's path, which `invalidateConfiguration` and the template save's `invalidateAllConfigurations` already cover.
3. **Lines.** `utils/composition-lines.ts` writes the JSON text itself, identical to `toJson` (`JSON.stringify(value, null, 2)`), and returns per line the pointer and the step, with the rules of section 7.3. Steps are looked up in a `Map` by pointer, never matched by position: `JSON.parse` moves integer-like keys first, so the lines can come in another order than the server's `Origins` (and than `sform`). The editor shows this text.
4. **Gutter.** `components/editor/setup/composition.ts` exports a CodeMirror extension, in the pattern of `setup/bindings.ts`:
   - a gutter that shows the step order on each line, in a full-contrast text color; the annotation type of the step's source tints only the background or a side bar of the marker (`color-mix(in oklab, var(--st-{code}) …, transparent)`, never a fixed color); a template step carries a cue that is not a color (for example a `T` suffix);
   - a hover tooltip: the step, the source (`Template · <Type>` or the configuration key), its version, and the node that applied it with the node it was merged into;
   - line decorations for the lines of the selected step, and a way for the view to set the selected step (a state effect) and to react to a click on a marker.
5. **View.** `components/configuration/ConfigurationComposition.vue` (props `annotationKey`, `effectiveHash`):
   - a short explanation: each line shows the step that set it; ancestors are merged first, each with its own ancestors, template and content, then the type template, then the own content; a shared ancestor is applied once per branch; a value repeated by a later document keeps the document that first set it;
   - an ordered list of the steps (the sub-sections), indented by the node's `Depth`, each with the step color and number, the type badge, the source, `via <key>` for a node merged into another ancestor (the key of its `MergedInto` node when that is not the target), `vN`, the presence (`No configuration`, `Empty`), the number of values it set, and an `Applied again` marker on a repeated source; a native `<details>` per step lists the paths it set, which is the text alternative to the gutter;
   - in each row the selection and the link are sibling controls, never nested: a toggle button (accessible name "Show the lines of step N", `aria-pressed`) and the source link or text;
   - links as in the lineage: an ancestor configuration opens its Edit route and keeps `q` (from `route.query`) only when `catalog.byKey.has(key)`, otherwise dimmed, struck-through text (the composition includes deleted ancestors); a template opens its Modeling detail when `useCan('edit')`, plain text otherwise; the target's own step has no link;
   - a read-only `CodeEditor` (JSON) with the extension; selecting a step in the list highlights its lines and scrolls the first one into view through the exposed `view` (`EditorView.scrollIntoView`) without moving the focus; a click on a gutter marker selects that step; all editor calls are optional (`editor.value?.view`), so the unit-test stub, which exposes nothing, is safe;
   - a polite live status line under the editor that names the step and source of the line holding the cursor or of the selected step ("Line 7: step 5 · Template · Execution"), because the gutter is `aria-hidden`;
   - `LoadingState`, the `EmptyState` of `ConfigurationEffective.vue` for a 404, `ErrorState` with retry, and a note when the document is empty;
   - when `Hash` and `effectiveHash` are both set and differ, the view first refetches the Effective query once; only if the hashes still differ it shows a warning that the Effective document and the composition were read at different times, with a `Refresh` action that invalidates the configuration's path (both queries). The Effective query keeps the 30-second `staleTime`, so a difference is often just an older Effective read;
   - wrappers that show keys or paths outside the editor carry `ph-no-capture`, like the editors.
6. **Mock.** `mock/configuration-handlers.ts` gets an exported `compositionOf(db, scope, key)` and a `GET ${api}/configuration/:key/composition` handler with the role and 404 rules of the mock Effective handler. It mirrors the mock Effective merge (each ancestor's own document as a node at depth 1, the key's type template, its own document) and returns `effectiveContent(…)` as `Content` with the mock Effective hash, so the mock never shows the stale warning. Its origins follow `deepMerge`, which replaces arrays: a write takes the origin of a value, unless it leaves a deep-equal scalar in place, which keeps the earlier origin, as on the server. The mock therefore never has a repeated source or a depth above 1.
7. **Tests.**
   - Vitest: `composition-lines.ts` (leaves, containers, arrays, empty containers, escaping, integer-like keys, identical text to `toJson`); the extension with `EditorState`/`EditorView` (markers per line, the template cue, decorations of the selected step, the selection effect); `ConfigurationComposition.vue` against the mock handlers (steps, links by role and catalog, the 404 state, the stale warning after one Effective refetch) and against a fixture shaped like the server's execution composition, served with `server.use(...)` (the subject's two nodes, `via`, `Depth` indentation, `Applied again`, a step that sets nothing); `ConfigurationEffective.vue` (the toggle writes `show`, the composition is fetched only once the view is shown). Highlight and scrolling are checked in the extension test and in Playwright, not under the editor stub.
   - Playwright (`test/e2e/configuration.spec.ts`, desktop and mobile): give `usg.northwind.invoicing` a property the execution template lacks (`window.storytellerMock.changeConfiguration`), open the Composition of `exe.northwind.invoicing.prod`, check the gutter values of the real `.cm-editor` (that property's line comes from the usage step, the `mode` line from the template step), and that selecting a step highlights its lines and updates the status line.
   - `test/e2e/accessibility.spec.ts`: a screen for the Composition view, light and dark (depth-1 nodes only, from the mock).
   - `app/components.d.ts`, `app/auto-imports.d.ts` and `app/typed-router.d.ts` are regenerated by a build and committed.

### 9. Documentation

- `docs/Platform/Storyteller/inheritance.md`: a section "Composition read" after "Other reads": the endpoint, nodes, steps, sources, presence (cache-only items are `Missing`), origins (an equal value keeps the first document), the replay rule, the non-associative example, the gate, the entity tag, and the consistency note of section 10.
- `docs/Platform/Storyteller/templating.md`: a row in "What each read returns", and `config composition` in "## CLI".
- `docs/42for.net/platform/configuration.md`: under "The effective document", the template in the merge order, the per-branch merge of shared ancestors, and where to see which document set each value.
- `docs/42for.net/platform/admin-ui.md`: the Effective tab and its Composition view in the Configuration section.
- `docs/42for.net/platform/using-the-platform.md`: the route in the list of reads.
- `src/Platform/ui.admin/README.md`: the e2e coverage list and the architecture bullets.

Specs and earlier reviews are not changed.

### 10. Consistency

The composition reads the stored documents without a snapshot: up to seven configuration items in two partitions and up to seven templates, read one after the other. A write between two reads can give a composition no single moment had. The Effective read can also serve a cache that an invalidation race left stale (`inheritance.md`, "Cache and descendant freshness"). Both cases show as a difference between the composition `Hash` and the Effective `Hash`, which the admin UI reports (section 8.5). The docs say so.

### 11. Tests of the backend (`Backend.CosmosDb/test`)

- **Pure** (`ConfigurationCompositionOriginsTests`, no collection attribute, no emulator):
  - `Mark` annotates every value token and leaves the input untouched; `Collect` throws on an unannotated value token;
  - pointers: nested objects, array indexes, `~0` and `~1`, no root entry, exactly one origin per value and container;
  - merges with `MergeInto`: object into object keeps the container's origin; an equal scalar (and an equal string) keeps the earlier origin, while `1` against `1.0` takes the incoming one; `null` on an existing property is ignored; `null` on an absent property takes the incoming origin; arrays keep their existing items (target duplicates included) and origins, skip incoming items deep-equal to an existing or an earlier incoming item, and append the others with their incoming origins; an array added to an absent property keeps its duplicates; object to scalar, scalar to object and array to object replace the whole subtree's origins;
  - the non-associative example: a nested group gives `a: {x: 1, y: 2}` with `/a` from the first step and `/a/y` from the parent's configuration step.
- **Emulator** (`CosmosConfigurationCompositionTests`, collection `CosmosDb`, own project names). The helpers of `CosmosConfigurationTemplateServiceTests` (`CreateExecutionAsync`, `CreateUnitOfExecutionAsync`, `HasCachedCalculationAsync`, `GetContainer`) are private, so the new class gets its own copies; existing tests are not rewritten:
  - equivalence, the acceptance gate: for a single chain, an execution (the NOK over USD example of `inheritance.md`), a unit of execution (the `retries` 1 over 9 example), and a template at an ancestor level, the composition's `Content` (property order included) and `Hash` equal those of `GetRawConfigurationAsync`;
  - the expected nodes, steps and origins for the execution: the subject's two nodes, `/currency` from the subject's configuration step via the context, `/residency` from the one via the usage, `/plan` from the usage, `/endpoint` from the context; and the nine nodes of the unit of execution;
  - the replay rule of section 1.4 over the response gives `Content`;
  - a composition before any Effective read equals the one after it, although the Effective read creates cache-only items for ancestors without a configuration (they stay `Missing`); the cold composition creates no cache item and changes no stored item;
  - `Missing` (no item, and a cache-only item), `Empty` (a stored `{}`, version 1 or more) and `Present`, and a missing template gives no template step;
  - null for a deleted annotation and for an unknown one without an item; an `Active` annotation without an item gives a composition with a `Missing` own step.

The emulator tests reset the local emulator's databases at start, as the whole test project does.

### 12. Phases and commits

1. `docs(platform): plan the composition of the effective configuration` (this spec)
2. **Phase 1**, `feat(platform): compose the effective configuration from its sources in Storyteller`: sections 1, 2, 3 and 11.
3. **Phase 2**, `feat(platform): answer the composition of a configuration in the Storyteller API`: sections 4 and 5.
4. **Phase 3**, `chore(platform): regenerate the SDKs from OpenAPI 0.9.N`: section 6.
5. **Phase 4**, `feat(platform): show the composition of a configuration with sform config composition`: section 7.
6. **Phase 5**, `feat(platform): show the composition of the effective configuration in the admin UI`: section 8.
7. **Phase 6**, `docs(platform): document the composition of the effective configuration`: section 9.
8. `docs(platform): review the composition of the effective configuration`: one review of all phases.

### 13. Differences from the CodeRabbit coding plan

The plan's choices that this spec keeps:

- a separate read-only walk next to the Effective calculation, which stays unchanged, with equivalence tests as the acceptance gate;
- a source catalog, ordered steps, a grouping that allows an exact replay, and RFC 6901 origins;
- the project-wide gate of `TemplateHttp`;
- a composition-specific weak entity tag;
- blame over JSON only, with the JSON/YAML toggle kept in the document view;
- a lazy query with `staleTime: 0`, a refetch on focus and a warning when the hashes differ, instead of a wider invalidation;
- a separate `config composition` command;
- SDKs regenerated only from the exported snapshot.

It differs in these points:

1. **The real merge attributes the values.** The plan re-implements the merge rules in a parallel walk next to `MergeInto`. This spec marks every token of each applied document with its step and lets `MergeInto` carry the marks (section 3). The origins cannot drift from the merge, including the null, union and replacement corner cases.
2. **A flat node list instead of a recursive tree.** The plan nests `Root.Parents`. Here `Nodes` refer to the node they are merged into, so no schema refers to itself, which the OpenAPI extension, NSwag and the Orval Zod output would all have to handle. Depth sits on the node, and a step points to its node instead of carrying `AppliedBy` and `Depth`.
3. **Which steps exist.** Every node has its configuration step, `Missing` included, so the whole lineage shows; a template step exists only for an existing template. The plan leaves this open.
4. **One spec and one review in `docs/Platform/Storyteller/specs/`**, as earlier features across the API, `sform` and the admin UI did, instead of companion specs and per-phase reviews in three folders.
5. **The blame listing of `sform` goes through `IExtendedConsole`**, so tests see it. `WriteJson` writes to the static console.
6. **No visual baseline.** The Composition view gets an e2e and an accessibility check; a screenshot baseline would need the Docker image and adds little over the gutter assertions.
7. **A `Values` column and count** per step in `sform` and the admin UI, which answers "what did this document change" without reading the gutter.
8. **A cache-only item is `Missing`.** The plan calls version 0 with a System stamp an unreliable discriminator and labels such items like any other. The write path itself treats version 0 as "an item that only cached the effective document" (`CosmosConfigurationService.cs:473`), every write gives version 1 or more, and a clear deletes the item. Without this rule the composition, its tag and both clients would change after the first Effective read, with no source changed.
9. **Blame follows the merge for equal values.** A later document that repeats a value does not take it over, because Newtonsoft keeps the existing token. The plan's walk would credit the later document; the server, the mock and the docs agree here on the first document.
10. **The admin UI view is usable without the gutter**: a per-step list of the paths it set, a live status line for the current line, and selection that keeps the focus in the list.

### Out of scope

- **Blame of the YAML view** and of the resolved document (bindings evaluated).
- **A composition of an older version** or of another view.
- **Invalidating the descendants' queries in the admin UI** when an ancestor's configuration is saved. A template save already invalidates every configuration query, compositions included. The Effective tab has the same ancestor gap; the composition refetches on focus and reports a mismatch.
- **Defects and oddities found while mapping, not changed here:**
  - **The Effective read of an existing item skips the state check.** `GetRawConfigurationAsync` returns any existing item, so a cache-only item of an annotation that was never created is served. The composition follows the same rule so the two reads agree.
  - **The mock Effective merge** (`deepMerge`) replaces arrays, lets `null` overwrite and flattens the ancestors, unlike the backend. The mock composition follows the mock Effective, so the two mock reads agree.
  - **The 404 copy of the Effective tab** ("Nothing is configured or inherited") is shown for an unknown or deleted annotation; an annotation without configuration answers `200` with `{}`.
  - **`config <key>` in `sform`** does not catch a 400 or 5xx `ApiException`, which ends in the "tool is broken" message.

### Merge notes

`feat/424-57` (open) regenerates the SDKs from `open.api.v0.9.43.json`, edits `templating.md`, `Definitions.cs` and `OpenApiExportTests`, adds an optional management-scope parameter to `CheckAccessToProjectAsync` and a scope column to the `ProjectWideHandlers` rows, and makes `sform` print the server's message for a 401 or 403 left to `Program`.

- The composition is a read and needs no management scope: `CheckAccessToProjectAsync(_access, organization, project)` stays valid, and its `ProjectWideHandlers` row takes no scope.
- Whichever branch lands second exports the document again on top of the other and regenerates both SDKs; the snapshot number follows the height of `Api.Functions/src` at that point. It also runs the `MachineRestrictionHttpTests`, `OpenApiExportTests` and `Cli` tests again.
