# JSON Schema revision

The behavioral contract this plan implements is [definitions.md](../definitions.md). The original design is [2026-04-15 Configuration JSON Schema API Endpoints](2026-04-15%20Configuration%20JSON%20Schema%20API%20Endpoints.md). Versioning follows [2026-09-28 Versioning of Configuration Templates](2026-09-28%20Versioning%20of%20Configuration%20Templates.md) and the guide in [templating.md](../templating.md).

Line numbers refer to the tree this plan was written against.

## Problem

Configuration schemas already document and constrain stored configuration, but five gaps keep the mechanism from matching the way views, configurations, and templates work.

1. **The schema is project-wide.** A type schema, an annotation schema, and a descendant-type schema are each stored once per project. Every view is checked against that same document. `ViewName` on the stored schema is empty, and the HTTP routes have no `{view}` segment. A schema change in service of the `default` view rejects, or is enforced on, the `rollout` view as well. Templates were moved to one document per project, view, and type for this reason. Schemas need the same isolation: one document per view, for every schema kind.

2. **Publishing a breaking change deadlocks.** Saving a schema runs the existing configurations through the new schema and throws when any fail. Saving a configuration runs the new content through the current schema and throws when it fails. Both failures are returned as `409`. There is no way to store the schema while old documents remain, and no way to store a document that the current schema refuses. The two writes cannot be ordered. The caller needs an explicit `force` on either write. Without it, the write still fails.

3. **The compliance check looks at the wrong document.** A schema save validates stored content against the single schema body being saved. Enforcement on a configuration save uses the combined schema (type, then ancestor descendant-type schemas, then the annotation schema). A type schema can be rejected for content that a more specific layer would accept after the merge, and a type schema can be accepted while the combined schema that will actually be enforced still rejects the content. The save-schema check has to evaluate the combined schema as it will stand after the write, and only for the view being written.

4. **Version and author exist only on the current document.** `ConfigurationSchema` and `ConfigurationSchemaEntity` already carry `Version` and `Author`. Every successful save increments `Version` and overwrites `Author`, including when the JSON is unchanged. Nothing copies the previous document. Delete drops the document. A later create starts again at version `1`. There is no version list, no fetch of an older schema, and no diff. Configurations and templates keep that history. Schemas should too.

5. **A configuration check drops the view.** `ValidateContentAsync` records `ViewName` as an empty string, and the configuration write path does not pass the view into schema resolution. Once schemas are per view, a write in `rollout` has to be checked against `rollout`.

Force is a parameter on the call, not a new role and not a flag stored on the document. The author of the new version is the record of who published a non-compliant schema or a non-compliant configuration.

## Current State

### What the original plan specified

[2026-04-15 Configuration JSON Schema API Endpoints](2026-04-15%20Configuration%20JSON%20Schema%20API%20Endpoints.md) specifies one schema per annotation type, stored for the whole project. The route is `v1/{org}/{project}/configuration-schema/{annotationType}`. The save path parses the body with NJsonSchema, loads every configuration of that type, and refuses the save when any configuration fails. The plan text says the schema is the same across views. The id sketched there (`{viewName}.cfs.{annotationType}`, partition `{projectName}.__schema__`) still contains a view, which the prose then rules out.

### What is implemented

The service grew three stored kinds plus a computed combination. All of them are project-wide.

`IConfigurationSchemaService` (`Backend.Core/src/Configuring/IConfigurationSchemaService.cs`):

| Method | Lines | Identity it uses |
| --- | --- | --- |
| `GetSchemaAsync`, `SetSchemaAsync`, `DeleteSchemaAsync` | 8–12 | Organization, project, annotation type |
| `GetAnnotationSchemaAsync`, `SetAnnotationSchemaAsync`, `DeleteAnnotationSchemaAsync` | 15–19 | Organization, project, annotation key |
| `GetDescendantTypeSchemaAsync`, `SetDescendantTypeSchemaAsync`, `DeleteDescendantTypeSchemaAsync` | 22–26 | Organization, project, annotation key, descendant type code |
| `GetCombinedSchemaAsync` | 29 | Organization, project, annotation key |
| `ValidateContentAsync` | 32 | Organization, project, annotation key, content |

No method takes a view. No method takes force.

`ConfigurationSchema` (`Abstractions.Annotations/src/Model/ConfigurationSchema.cs`, lines 5–16) has `AnnotationType`, `AnnotationKey`, `Version` (`ulong`), `Content` (`JObject`), and `Author`. `CombinedConfigurationSchema` (same folder) has `AnnotationKey`, `MergedContent`, and `AppliedSchemas`.

`ConfigurationSchemaEntity` (`Backend.CosmosDb/src/Entities/Configurations/ConfigurationSchemaEntity.cs`) adds `Content`, `Author`, and `Version` to `Entity`. There is no history entity and no state entity. `EntityIdPrefixTypes` has `ConfigurationSchema = "cfs"` and nothing for schema history or schema state.

Storage in `CosmosConfigurationSchemaService`:

| Kind | Partition | Id | `ViewName` |
| --- | --- | --- | --- |
| Type | `{project}.schema` (`GetSchemaPartitionKey`, line 587) | `cfs.{typeCode}` (line 597) | `""` (line 91) |
| Annotation | Annotation partition: `{project}.sbt.{subject}` or `{project}.rst.{responsibility}` (lines 602–607) | `cfs.{annotationKey}` (line 609) | `""` (line 178) |
| Descendant type | Same annotation partition | `cfs.dt.{descendantType}.{annotationKey}` (line 614) | `""` (line 277) |

Type codes are lower-cased. Descendant type codes must be in `AnnotationTypeCodes.ValidCodes`. Writes go through `UpsertWithOptimisticConcurrencyAsync` (line 553): read ETag, upsert with `IfMatchEtag`, retry up to three times, then throw `InvalidOperationException`. The API does not catch that exception, so a lost race becomes `500`. The version stored is `1` when the item is missing and `existing.Version + 1` otherwise, even when `Content` is deeply equal to the previous document. Delete is a point delete and keeps no history (lines 100–117, 187–204, 286–307).

### Combination and the ancestor list

`GetCombinedSchemaAsync` (lines 310–374) loads the type schema, every ancestor descendant-type schema from `AnnotationHierarchy.GetAncestorSchemaSources` (`Abstractions.Annotations/src/AnnotationHierarchy.cs`), and the annotation schema. It deep-merges them in that order (`DeepMergeSchemas`, line 653). Objects merge recursively. `required` arrays are unioned. Any other keyword on a later layer replaces the earlier one (lines 671–708). The result is cached nowhere. Schema reads are live.

The ancestor list is:

| Type | Sources, in merge order |
| --- | --- |
| `rst`, `sbt` | none |
| `unt` | responsibility / `unt` |
| `cnt` | subject / `cnt` |
| `usg` | responsibility / `usg`, then subject / `usg` |
| `exe` | responsibility / `exe`, subject / `exe`, usage / `exe`, context / `exe` |
| `uxe` | responsibility / `uxe`, subject / `uxe`, usage / `uxe`, context / `uxe`, execution / `uxe`, unit / `uxe` |

### When a write is rejected

`SetSchemaAsync` (lines 49–98), `SetAnnotationSchemaAsync` (lines 135–185), and `SetDescendantTypeSchemaAsync` (lines 227–284) each parse the body with `JsonSchema.FromJsonAsync`. A parse failure throws `ArgumentException`, which the API returns as `400`. They then scan existing `ConfigurationEntity` rows and throw `SchemaValidationException` when any non-empty `Content` fails the schema body being saved. The scan is the whole project:

- Type: id contains `.cnf.{typeCode}.`, any view (lines 406–450).
- Annotation: id ends with `.cnf.{annotationKey}`, any view (lines 452–496).
- Descendant: id contains `.cnf.{descendantType}.`, then the ancestor list is filtered in memory (lines 498–551).

Empty `Content` is skipped. The scan uses the candidate document alone. It does not build the combined schema.

`ValidateContentAsync` (lines 376–404) builds the combined schema and validates the content JSON. No combined schema means return. Failures throw `SchemaValidationException` with `ViewName` set to `""`. `SchemaValidationException` (`Backend.Core/src/Configuring/SchemaValidationException.cs`) carries `AnnotationKey`, `ViewName`, and the messages `$"{path}: {kind}"`.

`CosmosConfigurationService` calls `ValidateContentAsync` from three sites, all without a view and without a force flag:

| Site | Lines | When |
| --- | --- | --- |
| Create | 257–260 | New configuration, after `$remove` and `$patch`, when content has values |
| Update | 329–333 | After merge, `$remove`, and `$patch`, when content changed and has values |
| Patch | 378–381 | After JSON Patch, when content changed and has values |

`ConfigurationHttp` maps `SchemaValidationException` on PUT and PATCH to `409` with `SchemaValidationErrorResponse` (lines 178–190 and 253–265). The same mapping exists on the three schema PUT methods in `ConfigurationSchemaHttp`. DELETE of a schema does not consult configurations.

### API and CLI

Routes in `Api.Functions/src/Definitions.cs` lines 64–72:

```text
v1/{org}/{project}/configuration-schema/type/{annotationType}
v1/{org}/{project}/configuration-schema/{key}
v1/{org}/{project}/configuration-schema/{key}/type/{annotationType}
v1/{org}/{project}/configuration-schema/{key}/definition
```

There is no `{view}`, no `force` query, and no version routes. Reads accept `Configuration.Read`, `Configuration.Write`, `Default.Read`, or `Default.Write`. Writes require `Configuration.Write` or `Default.Write` and `AccountRole.Contributor`. Author comes from `request.GetAuthor()`.

The CLI has configuration commands and template commands under `sform story`. It has no schema commands. `ConfigSetCommand` and `ConfigEditCommand` have no `--force`. The CLI talks to `Sdk.NSwag`. `Sdk.Kiota` is generated from the same OpenAPI document.

### Versioning to copy

Template versioning is the closer copy: one logical document per project, view, and key, history in the same partition, a state row that remembers `LastVersion` across delete. The relevant pieces are `IConfigurationTemplateService`, `CosmosConfigurationTemplateService`, `GenerateTemplateHistoryEntity`, `GenerateTemplateStateEntity`, `TemplateConcurrencyException`, and `JsonContentDiffer` (`Backend.CosmosDb/src/Configuring/JsonContentDiffer.cs`). History ttl is 365 days. An unchanged document does not write. Diffs reuse `DiffResult`. Version `0` is an empty object. The version list reuses `ConfigurationVersion`.

Schemas differ from templates on purpose in this plan: a schema save replaces `Content` with the request body, and a schema save does not invalidate calculated configuration caches. Calculation never reads a schema.

### Tests that pin the current behavior

`Backend.CosmosDb/test/CosmosConfigurationSchemaServiceTests.cs` covers type, annotation, and descendant round-trips, combined merge order, `ValidateContentAsync` success, failure, and the no-schema no-op, and configuration create blocked or allowed by a type schema. Those tests call the project-wide methods and will be updated to pass a view.

## Proposed Changes

Delivery is split into two phases.

- **Phase A**, backend and API: sections 1–8.
- **Phase B**, SDK and CLI: sections 9–11.

Decisions that the rest of the plan follows:

- **D1. One document per view, for every kind.** Type, annotation, and descendant-type schemas move to `{project}.schema` with the view in the id. A write in one view does not read or reject configurations in another view.
- **D2. Legacy schema documents are ignored.** Items already stored (`cfs.{type}` in `{project}.schema`, `cfs.{key}` and `cfs.dt.{type}.{key}` in annotation partitions, `ViewName` empty) are not read, not copied, and not written. A miss on the view-scoped id is a missing schema. No project-wide document is applied in its place. The implementation does not migrate or delete those items; they stay in the container as unused data.
- **D3. Compliance uses the combined schema of the view after the write.** Schema save substitutes the candidate into the layer being saved and validates in-scope stored content against that combination. Configuration save validates the document about to be stored against the combined schema of its view.
- **D4. Force is explicit and defaults off.** Query `force=true` on schema PUT and on configuration PUT and PATCH. CLI `--force`. Absent or false preserves today's rejection. Force skips the compliance check only. An unparsable schema is still `400`. Force is not stored and does not add a role.
- **D5. History matches templates, without cache invalidation.** Unchanged content does not write. Delete keeps the version counter. Schema writes do not clear `CalculatedContent`.

---

## Phase A — Backend and API

### 1. Identity and storage

A schema is identified by organization, project, view, kind, and kind-specific key.

| Field | Value |
| --- | --- |
| Partition | `{project}.schema` for current, history, and state items of every kind |
| `ProjectName` | project |
| `ViewName` | view |
| `Content`, `Author`, `Version` | unchanged meaning |

Ids use a kind segment so a prefix listing of type `exe` cannot match an annotation key that starts with `exe.`:

| Kind | Current id | History id | State id |
| --- | --- | --- | --- |
| Type | `{view}.cfs.t.{typeCode}` | `{view}.csv.t.{typeCode}.{version}` | `{view}.css.t.{typeCode}` |
| Annotation | `{view}.cfs.a.{annotationKey}` | `{view}.csv.a.{annotationKey}.{version}` | `{view}.css.a.{annotationKey}` |
| Descendant type | `{view}.cfs.d.{descendantType}.{annotationKey}` | `{view}.csv.d.{descendantType}.{annotationKey}.{version}` | `{view}.css.d.{descendantType}.{annotationKey}` |

Examples in project `billing`: `default.cfs.t.exe`, `default.cfs.a.rst.invoicing`, `default.cfs.d.exe.rst.invoicing`, history `default.csv.t.exe.2`, state `default.css.t.exe`.

`EntityIdPrefixTypes` gains:

```csharp
public const string ConfigurationSchemaVersion = "csv";
public const string ConfigurationSchemaState = "css";
```

`cfs` stays. Kind letters `t`, `a`, and `d` are id segments, not new prefixes.

`AnnotationKey` and `Name` on the entity:

| Kind | `AnnotationKey` | `Name` |
| --- | --- | --- |
| Type | type code | type code |
| Annotation | annotation key | annotation key |
| Descendant type | ancestor annotation key | `dt.{descendantType}.{annotationKey}` |

Type codes are lower-cased and must be in `AnnotationTypeCodes.ValidCodes`, as today. Annotation keys are parsed with `AnnotationKey`, as today.

Documents at the old ids are ignored on every read and every write:

| Kind | Ignored id | Ignored partition |
| --- | --- | --- |
| Type | `cfs.{typeCode}` | `{project}.schema` |
| Annotation | `cfs.{annotationKey}` | the annotation partition from `GetAnnotationSchemaPartitionKey` |
| Descendant type | `cfs.dt.{descendantType}.{annotationKey}` | the same annotation partition |

A view-scoped create starts at version `1`. Nothing is read from those old items, so their version and author are not carried forward.

### 2. Entities and models

- **`ConfigurationSchemaEntity`**: no new fields. `ViewName` is the view. `Version` stays `ulong`.
- **New `ConfigurationSchemaHistoryEntity`** in `Entities/Configurations/`. Same shape as `GenerateTemplateHistoryEntity`: `Version`, `Content`, `Author`, `CreationTime`, `ttl` of 365 days. Add `GetExpirationTime()` beside the existing helpers in `ConfigurationHistoryEntityExtensions`.
- **New `ConfigurationSchemaStateEntity`**. `LastVersion` (`ulong`) only. Schemas do not invalidate calculated caches, so there is no `IsInvalidationPending` flag. The state row outlives delete and history expiry.

`ConfigurationSchema` gains `View` (`string`). `AnnotationType` and `AnnotationKey` stay optional and keep today's population rules: type schemas set `AnnotationType`; annotation schemas set `AnnotationKey`; descendant-type schemas set both. `CombinedConfigurationSchema` gains `View`.

New mapping file `Backend.CosmosDb/src/SchemaMappings.cs` with `ToConfigurationSchema` and `ToConfigurationVersion` for the current and history entities.

### 3. Service surface

`IConfigurationSchemaService` gains `view` on every method and `force` on the three saves:

```csharp
public interface IConfigurationSchemaService
{
    Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string view, string annotationType, JObject schemaContent, string author, bool force);

    Task<bool> DeleteSchemaAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema> SetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey, JObject schemaContent, string author, bool force);

    Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode);

    Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, JObject schemaContent, string author, bool force);

    Task<bool> DeleteDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode);

    Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(string organization, string project, string view, string annotationKey);

    Task ValidateContentAsync(string organization, string project, string view, string annotationKey, JObject content);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetSchemaVersionsAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema?> GetSchemaVersionContentAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion);

    // The same four version methods for annotation schemas (key instead of type)
    // and for descendant-type schemas (key and descendant type).
}
```

`IConfigurationService` gains `bool force = false` as the last parameter of `CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync`. Callers that omit it keep today's rejection.

New `SchemaConcurrencyException` in `Backend.Core/src/Configuring/`, modelled on `TemplateConcurrencyException`, carrying project, view, schema id, and the retry count.

### 4. Reads

Point-read the view-scoped id in `{project}.schema`. A miss returns null. There is no second read of an older id. `GetCombinedSchemaAsync` resolves each layer that way, then deep-merges as `DeepMergeSchemas` does today. The merge function stays. An annotation in a view with no layer at all still returns null from the combined read, and `ValidateContentAsync` still returns without throwing.

`ValidateContentAsync` passes the real view into the error (`ViewName` = the view argument) when the combined schema rejects the content.

### 5. Saving a schema

Shared by the three set methods.

1. Parse the body with `JsonSchema.FromJsonAsync`. Failure throws `ArgumentException` (`400`), whether or not `force` is true.
2. Read the current view-scoped item and its ETag. A miss is a create.
3. When the current item exists and `JToken.DeepEquals` says `Content` is unchanged, return it. Do not check compliance and do not bump `Version`.
4. Build the compliance list (section 6) unless step 3 returned.
5. When the list is non-empty and `force` is false, throw `SchemaValidationException`.
6. When the list is non-empty and `force` is true, do not throw. Log each entry at warning, with organization, project, view, annotation key, and messages.
7. Allocate the version: `1` when no current item and no state row exist; otherwise `max(current.Version, state.LastVersion) + 1` on create and `current.Version + 1` on update. A create after delete uses `state.LastVersion + 1` when the history has expired, matching templates.
8. Write one transactional batch: history of the previous current item (updates only), the upsert of the schema, and the state row with `LastVersion` set to the new version. The upsert and the state update carry the ETags that were read. On `412` or a history `409`, re-read and retry up to three times, then throw `SchemaConcurrencyException`.

The request body replaces `Content`. There is no `MergeInto`, `$remove`, or `$patch` on a schema.

### 6. Compliance scan

`CollectComplianceErrorsAsync` is the single implementation behind the three saves.

Resolve the combined schema for a candidate by calling the same combiner as `GetCombinedSchemaAsync`, with one layer overridden by the unsaved document:

| Save | Override | Rows scanned in the view |
| --- | --- | --- |
| Type `T` | The type layer for `T` | `ViewName == view` and id starts with `{view}.cnf.{T}.` |
| Annotation `K` | The annotation layer for `K` | id equals `{view}.cnf.{K}` |
| Descendant type `D` on ancestor `A` | That ancestor's descendant-type layer | `ViewName == view` and id starts with `{view}.cnf.{D}.`, then keep rows whose `GetAncestorSchemaSources` contains `(A, D)` |

The query stays cross-partition, as today's scan is. It adds the view predicate, which today's scan does not have. Empty `Content` is skipped. Each remaining row is validated against its own combined schema (the override plus that row's other layers, resolved from view-scoped ids in the same view). Failures become `SchemaValidationError` with that row's `AnnotationKey` and `ViewName`.

A type-schema save for `exe` therefore checks each execution in the view against type + that execution's ancestor descendant schemas + that execution's annotation schema, with the candidate standing in for the type layer. An execution that fails only because of its annotation schema is reported. An execution that fails the candidate type schema in isolation and passes once a later layer replaces a keyword is not reported.

### 7. Delete and version reads

Delete point-reads the view-scoped item. Missing returns false. Present: one batch writes the history row of the current version, deletes the current item, and updates `LastVersion` on the state row if needed. The next read of that id is a miss. Delete does not take `force`.

Version list, version content, and diffs follow `CosmosConfigurationTemplateService` and `JsonContentDiffer`:

- List history by the kind's id prefix, ordered by `Version`, then append the current item with `ExpirationTime = DateTimeOffset.MaxValue`.
- Content of version `N` is the history row, or the current item when its `Version` is `N`. Unknown returns null at the service and `404` at the API.
- Diff of version `N` is `N-1 → N`. The two-argument method diffs any pair. Version `0` is an empty object. An unknown version throws `InvalidOperationException`, mapped to `404` the way template diffs are.

### 8. Configuration writes and HTTP

`CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync` pass `key.ViewName` into `ValidateContentAsync`. When `force` is true they skip that call. The empty-content skip and the no-schema skip stay. Force does not skip JSON parsing, the missing-configuration error on patch, merge, or cache invalidation.

Routes in `Definitions.Routes.ConfigurationSchema.V1` gain `{view}` and drop the project-only routes. Old URLs are not kept as aliases.

```text
v1/{org}/{project}/{view}/configuration-schema/type/{annotationType}
v1/{org}/{project}/{view}/configuration-schema/{key}
v1/{org}/{project}/{view}/configuration-schema/{key}/type/{annotationType}
v1/{org}/{project}/{view}/configuration-schema/{key}/definition
```

Each of the three stored resources gains the template version suffixes: `/versions`, `/versions/{version}`, `/versions/{version}/diff`, `/versions/{version}/diff/{versionFrom}`. The combined route does not.

`Definitions.Parameters` gains `Force = "force"`. OpenAPI declares it as an optional boolean query parameter, default false, on:

- PUT of the three schema resources
- PUT and PATCH of configuration (`ConfigurationHttp`)

`ConfigurationSchemaHttp` passes the route view and the query value through. Status codes:

| Status | Body | When |
| --- | --- | --- |
| 200 | `ConfigurationSchema` or `Configuration` | Saved, including a forced save |
| 400 | `ErrorResponse` | Bad JSON, schema the parser rejects, bad type or key |
| 404 | empty or `ErrorResponse` | Missing schema, missing version, missing configuration on patch |
| 409 | `SchemaValidationErrorResponse` | Compliance failed and force is false |
| 409 | `ErrorResponse` | `SchemaConcurrencyException` |
| 500 | `ErrorResponse` | Storage failure |

Security scopes and the `Contributor` requirement are unchanged. GET combined and the version reads use the read scopes.

`CosmosConfigurationSchemaServiceTests` is rewritten against the view-scoped methods. Add cases for:

- a schema in `default` does not affect a configuration write in another view
- schema save returns `SchemaValidationException` when one configuration in the view fails the combined schema, and ignores another view
- schema save with `force: true` persists and increments `Version` when that configuration fails, and still throws `ArgumentException` for an unparsable body
- a deeply equal schema PUT does not increment `Version`
- delete then create continues after `LastVersion`
- version list, version content, and a diff of `N-1 → N`
- a document at an old project-wide id is not returned by get, is not part of the combined schema, and does not reject a configuration write
- configuration create and patch throw without force and persist with force
- `ValidateContentAsync` puts the view on the error
- combined order is unchanged: type, ancestors, annotation, with `required` unioned

---

## Phase B — SDK and CLI

### 9. Clients

Regenerate `Sdk.NSwag` and `Sdk.Kiota` from the OpenAPI document after the route and `force` changes. The CLI uses the NSwag client. This is a breaking client change: schema methods gain a view argument, the project-only schema URLs disappear, and configuration PUT and PATCH gain `force`.

### 10. Schema commands

Add a `schema` command group under `sform story`, registered from `StorytellerListCommand` the way `TemplateGetCommand` is. The view is `Context.ViewName`.

| Command | Calls |
| --- | --- |
| `schema type get\|set\|edit\|delete\|versions\|diff {annotationType}` | Type-schema endpoints |
| `schema annotation get\|set\|edit\|delete\|versions\|diff {annotationKey}` | Annotation-schema endpoints |
| `schema descendant get\|set\|edit\|delete\|versions\|diff {annotationKey} {annotationType}` | Descendant-type endpoints |
| `schema definition {annotationKey}` | Combined GET |

`set` takes `-i|--import` and sends the file as the schema body (replace, not merge). `edit` loads the current schema into the editor and PUTs the result; a missing schema is reported the way `get` reports it. `set` and `edit` take `--force` and pass `force=true`. `diff` follows `TemplateDiffCommand`, including `--format`.

`CommandNames` gains `SCHEMA = "schema"`.

### 11. Configuration commands

`ConfigSetCommand` and `ConfigEditCommand` gain `--force` and pass it to configuration PUT. JSON Patch issued through the configuration PATCH endpoint has to send the same query; the CLI has no separate patch command today, and `$patch` inside `set` already travels through `CreateOrUpdateConfigurationAsync`, which honors `force`.

A forced schema save prints the new version and a line that existing configurations were not required to comply. A rejected save prints the annotation key, view, and messages from `SchemaValidationErrorResponse`.

## Breaking changes

- Schema URLs gain `{view}`. Clients of the project-only routes need the new paths.
- `IConfigurationSchemaService` method signatures gain `view` and, on saves, `force`.
- Configuration PUT and PATCH grow an optional `force` query. Old callers that omit it keep the rejection behavior.
- A schema PUT that repeats the current JSON no longer increments `Version`.
- A schema save can now succeed while stored configurations in the view fail the schema, when the caller passes `force=true`. Those configurations stay readable. Their next ordinary write is rejected until they comply or are themselves forced.
- Existing project-wide schema items stay in the container and stop affecting reads and writes. A view has a schema only after a view-scoped document is written. Until then, configuration writes in that view are not schema-checked.

## Out of scope

- Copying or deleting the ignored project-wide schema items.
- Validating calculated configuration, resolved configuration, or templates.
- A persisted "this version was forced" flag, and a role beyond `Contributor` for force.
- Clearing `CalculatedContent` on a schema write.
- Merging a schema PUT into the previous schema document.
- A project-level schema layer. The only schema documents the service reads are the view-scoped ids in section 1.
