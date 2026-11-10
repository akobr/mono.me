# Restore a Previous Version of a Configuration, Template or Schema

Linear 424-221. One generic "restore version N" operation for the three versioned documents of Storyteller: configurations, templates, and schemas (type, annotation and descendant-type schemas).

## Problem

Configurations, templates and schemas keep a version history. Clients can list the versions, read them and diff them, but they cannot restore one. When someone deletes a configuration by mistake, or saves a bad change, the only way back is by hand:

```sh
sform story config get <key> --version N --content-only > old.json
sform story config set <key> old.json
```

That is not atomic, it merges instead of replacing (`config set` merges into the stored document), it reinterprets a stored top-level `$remove` or `$patch` key as a directive, and the audit records an ordinary update. Templates and schemas have the same gap. The MCP server's `revert_configuration` does the same on the client (a read, then a JSON Patch with `test` operations), and has no twin for templates or schemas.

The owner's decisions (Linear 424-221):

1. **Restoring the current version changes nothing.** No new version, no audit entry. The response says that nothing changed.
2. **A purged annotation cannot be restored from.** The response is `404`, because the annotation no longer exists. Its orphaned versions stay readable until they expire.
3. **One generic pattern** for configurations, templates and schemas: the same route shape, status codes, CLI option and audit record.

## Current State

### Storage and version numbering

- **Configurations** (`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`) live in the annotation's partition: the row `{view}.cnf.{key}` (`ConfigurationEntity`: `Version`, `Content`, `Modified`, the cache, `PendingInvalidationId`, `AnnotationDeletionId`) and the archives `{view}.cfv.{key}.{version}` (`ConfigurationHistoryEntity`, a 365-day `ttl`).
  - A write is `WriteConfigurationAsync(key, value, actor, force, merge)` (392-494): per attempt `ReadForWriteAsync` (1393-1406: the row with its ETag, `EnsureAnnotationNotDeletedAsync`, and the completion of pending invalidations), then either the create (no row: version `GetHighestArchivedVersionAsync + 1`) or `TryWriteNextVersionAsync` (1236-1291: the archive of the stored version and the replacement of the row in one batch guarded by the ETag; `PlanArchiveAsync` picks the numbers). Up to four attempts, then `ConflictException(Conflict)`. After the commit: `AuditConfigurationAsync` (1641-1657, best effort) and `CompleteInvalidationAsync` (the descendants' caches).
  - `CreateOrUpdateConfigurationAsync` merges (`merge: true`); the import replaces (`merge: false`). Both run `RemoveRequested` and `ApplyPatchRequested` on the input, so a stored literal `$remove` or `$patch` key (a JSON Patch can write one) would be read as a directive.
  - An equal document writes nothing (467-473). The schema is checked before the write unless `force` (`IConfigurationSchemaService.ValidateContentAsync`).
  - A missing annotation is not refused: a `PUT` on an unknown or purged key creates a row. A soft-deleted one answers `409 AnnotationDeleted` (`EnsureAnnotationNotDeletedAsync`, 1625-1637).
  - Reads of versions: `GetConfigurationVersionsAsync` (269-319) hides everything of a soft-deleted annotation, lists the written archives below the current version and the current one; `GetConfigurationVersionContentAsync` (321-359) also returns an archive above the current version (a leftover the list hides). A purged annotation (`AnnotationItemState.Missing`) keeps its archives listed and readable for their 365 days (`CosmosAnnotationLifecycleTests.Purge_RemovesTheTreeAndItsAuditsButKeepsTheVersions`).
  - The soft delete, the re-create and the purge put the last content into the archive of its version (`CosmosAnnotationService.ToHistorySnapshot`, 357-371).
- **Templates** (`CosmosConfigurationTemplateService.cs`) are one document per view and type code, in the partition `{project}.template`: the current `{view}.gen.{type}`, the history `{view}.gnv.{type}.{version}` and the state `{view}.gns.{type}` (`LastVersion`, `IsInvalidationPending`). `WriteTemplateAsync` (404-493) merges or replaces, `TryWriteNextVersionAsync` (507-538) archives and replaces in one batch with the state write, and `InvalidateAsync` clears the calculated configurations of the type and its descendant types. A delete archives the last version; a later write continues at `LastVersion + 1`. Templates have no schema, so no `force`. No annotation owns a template.
- **Schemas** (`CosmosConfigurationSchemaService.cs`) are all in the partition `{project}.schema`: the current `{view}.cfs.{suffix}`, the history `{view}.csv.{suffix}.{version}`, the state `{view}.css.{suffix}` (`LastVersion`), with the suffix `t.{type}`, `a.{key}` or `d.{type}.{key}` (`SchemaIdentity`, 1085-1133). `SaveAsync` (443-538) checks that the document is a usable schema (`EnsureValidSchemaAsync`, always, `400`), checks the stored configurations it applies to (`CollectComplianceErrorsAsync`, skipped by `force`), and writes the history, the new current item and the state in one batch. `ReadVersionAsync` (629-651) reads a history item or the current one. An equal document writes nothing. Schema writes never check the annotation, and the soft delete and purge of an annotation do not touch its schemas. Schema changes clear no caches.
- **Audit.** Only annotations have an audit (`AnnotationAuditEntry` in `{view}.adt.{key}`, `Abstractions.Annotations/src/Model/AnnotationAudit.cs`). Configuration writes append `ConfigurationChanged` or `ConfigurationDeleted` with `ConfigurationVersion`. Templates and schemas only stamp `Modified` on each version: the audit spec left them out on purpose ([Soft-delete and audit of annotations](2026-10-11%20Soft-delete%20and%20audit%20of%20annotations.md), "Out of scope").

### API

- Routes in `Api.Functions/src/Definitions.cs`: `Configuration.V1.Version` (153), `ConfigurationSchema.V1.Schema{Type,Annotation,DescendantType}Version` (186, 191, 196), `Template.V1.Version` (246), with `RouteIds` per operation. "Restore" already names the undeletes `RestoreAnnotations` (`…/annotations/deleted/restore`) and `RestoreKeyValues` (`…/key-values/restore`).
- Access of the writes: configurations `CheckAccessToAnnotationAsync(…, Contributor, Configuration.Write)` (`ConfigurationHttp.SetConfiguration`); type schemas `CheckAccessToProjectAsync(…, Contributor, Configuration.Write, Schema.Write)`, annotation and descendant-type schemas the same on the annotation; templates `CheckAccessToProjectAsync(…, Contributor, Configuration.Write, Template.Write)`.
- Statuses of the writes today:
  - a configuration that violates its schema, and a schema that stored configurations violate: **`409`** with `SchemaValidationErrorResponse`; `force` stores it anyway. A schema that cannot be used (`InvalidSchema`, `ExternalSchemaReference`, `SchemaValidationTimeout`) is `400`.
  - concurrent changes: `409` (`Conflict` for configurations; templates and schemas answer `ErrorResponse` without a code).
  - `412` only for a failed JSON Patch `test` (`PatchTestFailed`): configurations, templates and schemas take no `If-Match`.
  - `SchemaValidationException`, `SchemaConcurrencyException` and `TemplateConcurrencyException` are mapped by the handlers, not by `ErrorResponseMapping`.
- `OpenApiExportTests` writes the document; `Sdk.NSwag/open.api.v0.10.31.json` and `sdk.typescript/open.api.v0.10.31.json` are the newest snapshots.

### Clients

- `sform story config|template|schema type|annotation|descendant` have `versions`, `diff` and `get --version`; no restore. `story restore` and `story kv restore` undelete. `-v` is the view, so `--version` is long-only.
- `Cli.AutomationTests`: `CommandCoverageTests` fails for a command, option or argument without a `[Covers]`, and `CatalogueTests` for a test without its Testiny case in `docs/Platform/Cli.AutomationTests/test-cases.md` (`cli-checks.yml` runs both on every pull request).
- MCP: `revert_configuration` (`Mcp/src/Tools/ConfigurationTools.cs:143-167`) restores on the client; templates and schemas have none.
- Admin UI: `VersionsView`/`VersionTimeline` serve configurations, templates and the three schema kinds through a `VersionSource`; they can view and compare a version, not restore it. `app/utils/audit.ts` keeps three exhaustive `Record<AnnotationAuditEntryAction, …>` maps.

## Proposed Changes

### 1. The restore rules (all three documents)

A restore of version N:

1. **Checks its target.**
   - A configuration, an annotation schema and a descendant-type schema belong to an annotation: a soft-deleted one answers `409 AnnotationDeleted` (restore the annotation first), a missing one (unknown, purged or expired) `404 AnnotationNotFound` (decision 2). The deleted check comes first, as the version reads of a deleted annotation are hidden.
   - A descendant-type schema checks the pair first, as its `PUT` does: a type that cannot descend from the key answers `400 InvalidKey`, also with `force`.
   - Templates and type schemas belong to no annotation, so these checks do not apply.
2. **Finds version N** by the rules of the version list: a version the list would show. Unknown, expired (past its `ttl`), never written, or hidden (an archive of a configuration above its current version) answers `404` with the new `ErrorCode` `VersionNotFound`.
3. **Compares it with the current document.**
   - N is the current version: nothing changes (decision 1).
   - The current document equals N's content: nothing changes either, as every write treats an equal document. The response names the current version.
   - Either way: no new version, no audit entry, `200` with `Outcome` `Unchanged`. A pending cache invalidation left by an earlier write is finished, as an equal write does.
4. **Otherwise writes N's content as a new version**, whole and verbatim: no merge, no `$remove`/`$patch` processing. History stays append-only, the numbering continues (`LastVersion + 1` or the next archive number), and the new version is stamped with the caller (`Modified`) and records `RestoredFrom = N`. A document that is deleted (a cleared configuration, a deleted template or schema) is brought back this way: that is the main use.
5. **Checks the content as the document's write does**, before anything is written:
   - a configuration against its combined schema, unless `force`: a violation is `409 SchemaValidationErrorResponse`, a schema that cannot be used `400`;
   - a schema must be a usable schema (`400`, also with `force`, e.g. a version stored before the local-reference rule), and the stored configurations it applies to must comply, unless `force` (`409 SchemaValidationErrorResponse`);
   - a template is not checked.
6. **Concurrency** as the writes: every attempt reads the current document with its ETag and writes guarded by it; after four lost attempts `409 Conflict`. There is no `412`: these documents take no `If-Match`.
7. **Side effects** as the writes: a configuration restore finishes the invalidation of its descendants' caches, a template restore clears the calculated configurations of its type; schemas clear nothing (as schema writes).

Status codes, all five routes:

| Result | Response |
| --- | --- |
| Restored | `200`, `Outcome` `Restored`, `Version` the new version |
| N is the current version, or the current document equals it | `200`, `Outcome` `Unchanged`, `Version` the current version |
| Invalid key, type code or pair (`InvalidKey`), a schema version that is no usable schema | `400` |
| No such version, or expired (`VersionNotFound`) | `404` |
| Annotation unknown or purged (`AnnotationNotFound`) | `404` (configurations, annotation and descendant-type schemas) |
| Annotation soft-deleted (`AnnotationDeleted`) | `409` (the same three) |
| The content violates a schema (configuration), or configurations violate the restored schema | `409` with `SchemaValidationErrorResponse`; `force=true` stores it anyway |
| Concurrent changes (`Conflict`) | `409` |

**Deviation from the issue:** the issue asks for `400` on a schema violation and `409`/`412` for concurrency. Every write of these documents answers a schema violation with `409 SchemaValidationErrorResponse` (and its clients, `sform` and the admin UI, are built on it), and none of them has a precondition, so the restore keeps the existing contract: `409` for a violation, `400` only for an unusable schema, and no `412`.

### 2. Models (Abstractions.Annotations)

- **`VersionRestoreResult`** (`Model/VersionRestore.cs`), the one response of every restore:

  ```csharp
  [JsonConverter(typeof(StringEnumConverter))]
  public enum VersionRestoreOutcome { Restored = 0, Unchanged }

  public record class VersionRestoreResult
  {
      public required VersionRestoreOutcome Outcome { get; init; }
      // The version whose content was asked for.
      public required ulong FromVersion { get; init; }
      // Restored: the new version; Unchanged: the current version.
      public required ulong Version { get; init; }
      // The write of Version.
      public required AuditStamp Modified { get; init; }
  }
  ```

- **`ConfigurationVersion.RestoredFrom`** (`uint?`): the version a restore copied into this one; null for any other write. It is how a template or schema restore is recorded (they have no audit), and the version lists of all three documents show it.
- **`AnnotationAuditAction.ConfigurationRestored`**, appended after `ConfigurationDeleted`, and **`AnnotationAuditEntry.FromVersion`** (`ulong?`): a configuration restore appends `ConfigurationRestored` with `ConfigurationVersion` (the new version) and `FromVersion` (N), best effort after the write, as `ConfigurationChanged`. `Restored` keeps its meaning (the undelete of an annotation).

### 3. Services (Backend.Core)

- `ErrorCodes.VersionNotFound`.
- `IConfigurationService.RestoreConfigurationVersionAsync(FullKey key, uint version, AuditActor actor, bool force = false)`.
- `IConfigurationTemplateService.RestoreTemplateVersionAsync(string organization, string project, string view, string annotationType, uint version, AuditActor actor)`.
- `IConfigurationSchemaService`: `RestoreSchemaVersionAsync(organization, project, view, annotationType, uint version, AuditActor actor, bool force)`, `RestoreAnnotationSchemaVersionAsync(organization, project, view, annotationKey, uint version, AuditActor actor, bool force)` and `RestoreDescendantTypeSchemaVersionAsync(organization, project, view, annotationKey, descendantTypeCode, uint version, AuditActor actor, bool force)`; the annotation key stays the fourth argument, as the test tables of the API read it.
- All return `VersionRestoreResult` and throw `NotFoundException` (`AnnotationNotFound`, `VersionNotFound`), `ConflictException` (`AnnotationDeleted`, `Conflict` for configurations), `InvalidInputException` and `SchemaValidationException` as the writes; templates and schemas keep their own concurrency exceptions.
- The fakes of `Backend.Core/test/Transferring` get `NotSupportedException` stubs.

### 4. Cosmos DB

- **Configurations**, `CosmosConfigurationService.Restore.cs`:
  - `AnnotationItemStore.ReadStateAsync`: `Deleted` → `409 AnnotationDeleted`, `Missing` → `404 AnnotationNotFound`.
  - Per attempt: `ReadForWriteAsync`; when the row is a written version, N equal to its version is `Unchanged` and N above it is `VersionNotFound`; otherwise the archive N (read once; a written version by `IsWrittenVersion`) or `VersionNotFound`; the equal-content no-op; the schema check unless `force`; then the create (no row: `GetHighestArchivedVersionAsync + 1`) or `TryWriteNextVersionAsync`, both with `RestoredFrom = N`.
  - After the commit: `AuditConfigurationAsync(…, ConfigurationRestored, M, fromVersion: N)`, then `CompleteInvalidationAsync`.
  - `ConfigurationEntity` and `ConfigurationHistoryEntity` get `RestoredFrom` (`ulong?`); `ToHistory`, `ToHistorySnapshot` and the version mappings carry it. `TryWriteNextVersionAsync` sets it from a new parameter, so an ordinary write after a restore does not inherit it (the row is copied with `with`).
- **Templates**, in `CosmosConfigurationTemplateService`: version N from the history item (read once) or the current item; per attempt the current item and the state with their ETags; the no-ops (finishing a pending invalidation); then the create batch with the state write (`LastVersion + 1`) for a deleted template, or `TryWriteNextVersionAsync` with `RestoredFrom`. The entities and `ToHistory` carry `RestoredFrom`; the merge write resets it.
- **Schemas**, in `CosmosConfigurationSchemaService.Restore.cs`: the pair rule and the annotation state for the two keyed kinds, version N by `ReadVersionAsync` (once), then a loop shared with `SaveAsync`: the no-ops, `EnsureValidSchemaAsync` (once), the compliance check unless `force`, the batch with `RestoredFrom`. The entities and `ToHistory` carry `RestoredFrom`.

### 5. API

Five `POST` functions, bodyless, on `…/versions/{version}/restore`:

| Operation | Route | Access (as the document's write) | `force` |
| --- | --- | --- | --- |
| `RestoreConfigurationVersion` | `…/configuration/{key}/versions/{version}/restore` | Contributor on the annotation; `Configuration.Write` | yes |
| `RestoreTemplateVersion` | `…/template/{annotationType}/versions/{version}/restore` | Contributor; `Configuration.Write` + `Template.Write` | no |
| `RestoreSchemaVersion` | `…/configuration-schema/type/{annotationType}/versions/{version}/restore` | Contributor; `Configuration.Write` + `Schema.Write` | yes |
| `RestoreAnnotationSchemaVersion` | `…/configuration-schema/{key}/versions/{version}/restore` | Contributor on the annotation; `Configuration.Write` + `Schema.Write` | yes |
| `RestoreDescendantTypeSchemaVersion` | `…/configuration-schema/{key}/type/{annotationType}/versions/{version}/restore` | Contributor on the annotation; `Configuration.Write` + `Schema.Write` | yes |

- `Definitions.Routes`: `Configuration.V1.VersionRestore = $"{Version}/restore"`, `Template.V1.VersionRestore`, `ConfigurationSchema.V1.Schema{Type,Annotation,DescendantType}VersionRestore`, so the schema key keeps its constraint; and the five `RouteIds`. The operations carry the document's tag, so the SDK methods land on the existing clients.
- Handlers in `ConfigurationHttp`, `TemplateHttp` and `ConfigurationSchemaHttp.Versions.cs`: the access check, the key or type validation, the service call, `200` with `VersionRestoreResult`; `StorytellerException` through `ErrorResponseMapping`, `SchemaValidationException` as `409 SchemaValidationErrorResponse`, the template and schema concurrency exceptions as `409` with `ErrorCode` `Conflict`, `ArgumentException` as `400`.
- OpenAPI: `200`, `400`, `401`, `403` (the scope texts of the writes), `404`, `409` (`SchemaValidationErrorResponse` where a schema applies), `500`.
- Tests: `RouteTableTests`, `RouteDispatchTests`, `OpenApiExportTests.MachineOperations`, `MachineRestrictionHttpTests.Handlers` (keyed: configuration, annotation and descendant-type schema; project-wide: template and type schema), `TemplateHttpTests.Writes`, `ConfigurationSchemaAuthorizationTests.Writes`, and handler tests of the statuses.
- `Api.Web` has none of these documents and does not change.

### 6. SDKs

The OpenAPI document is exported again (`open.api.v0.10.<n>.json` in `Sdk.NSwag`, replacing the copy in `sdk.typescript`), `ApiSdk.g.cs` is regenerated with NSwag 14.6.3 and `sdk.typescript/src/generated` with `npm run generate`. The new methods are `Restore{ConfigurationVersion,TemplateVersion,SchemaVersion,AnnotationSchemaVersion,DescendantTypeSchemaVersion}Async` on `IConfigurationsApiClient`, `ITemplatesApiClient` and `ISchemasApiClient`, and `restore…` functions with `useRestore…` mutations in TypeScript.

**Compatibility:** a client generated before this change cannot read an annotation history that holds `ConfigurationRestored` (NSwag's `StringEnumConverter` throws on an unknown value), and an older API rolled back over such an audit item fails to load it. The platform is pre-1.0 and its clients ship with the API; the regenerated SDKs, `sform`, the MCP server and the admin UI understand the action in this change.

### 7. sform

| Command | Arguments | Options |
| --- | --- | --- |
| `sform story config restore <key> --version N` | the annotation key | `-f\|--force` (store it although it violates the schema) |
| `sform story template restore <type> --version N` | the type code | — |
| `sform story schema type restore <type> --version N` | the type code | `-f\|--force` (existing configurations need not comply) |
| `sform story schema annotation restore <key> --version N` | the annotation key | `-f\|--force` |
| `sform story schema descendant restore <key> <type> --version N` | the key and the type code | `-f\|--force` |

- `--version` is required (`int?`, long-only as in `get`); missing or negative, the command says so and exits with `2`. The key or type is validated as by the other commands.
- `Restored`: `Restored v{N} of the configuration for '<key>' as v{M}.` (the template for `'<type>'`, the schema for `'<subject>'`), exit `0`.
- `Unchanged`: `v{N} is already the current version of …` when `Version == N`, otherwise `v{N} equals the current version v{M} of …; nothing changed.`, exit `42` (nothing to do), as `config patch` and `edit` exit without a change.
- Errors: `SchemaValidationConsole.WriteWriteError`: a schema violation lists the configurations and exits `2`; any other refusal prints the server's message and error code (`VersionNotFound`, `AnnotationNotFound`, `AnnotationDeleted`, `Conflict`) with its exit code (`2`, `44`, `3`); a `401` goes to `Program`.
- `story history` names the new action `Config restored` and its change `config v{M} from v{N}`. The versions tables get a `Restored from` column when a listed version was restored.
- Unit tests per command (`ConfigRestoreCommandTests`, the template tests, the layer theory of `SchemaOperationsTests`), `ApiRefusalExitCodeTests`, `AuditConsoleExtensionsTests`.
- `Cli.AutomationTests`: new cases in the Testiny project `2SC` and the catalogue, with `[Covers]` of every new command, argument and option (`--force` and `-f` each); the context cases `CFG-CTX-01`, `TPL-TPL-09` and `SCH-CTX-01` cover `--projectKey`, `-v` and `--view` of the new commands. The counts of the catalogue, its docs and `CLAUDE.md` follow.

### 8. MCP server (nice to have)

- `revert_configuration` calls `RestoreConfigurationVersionAsync` instead of its client-side read and patch: atomic, audited as `ConfigurationRestored`, without the directive guard (the server copies the version verbatim). It returns the `VersionRestoreResult` with `Changed`.
- New `revert_template` and `revert_schema` (with the `level` of the schema tools), on their restore routes. The names keep "revert", as `restore_annotations` undeletes.
- `ManagementToolsHostTests`, the tool tests and `mcp.md` follow; `get_annotation_history` names the restore.

### 9. Admin UI (nice to have)

- `VersionSource` gets an optional `restore(version, force)`; `VersionTimeline` a **Restore** button on every version that is not current and not expired, for Contributors (`useCan('edit')`), with a confirmation. The configuration, template and schema sources implement it; a configuration or schema violation offers **Restore anyway** (`force`), as **Save anyway** does. Success toasts `Restored vN as vM` (or that nothing changed) and invalidates the document, its versions and, for a configuration, the annotation history.
- `utils/audit.ts`: `ConfigurationRestored` is labelled **Configuration restored**, and `AnnotationHistory` links `Configuration restored as vM from vN`.
- The mock API answers the five routes with the same rules (its document store keeps the versions of a deleted document so that a restore can bring it back), and appends `ConfigurationRestored`.
- Unit tests of the button, the confirmation, the force path and the mock.

### 10. Documentation

- `annotation-lifecycle.md` (the audit table, the routes of a deleted annotation, the error codes), `inheritance.md` (writes), `templating.md`, `definitions.md` (routes, `Force`, `Versions`, CLI), `audit-operations.md`, `authentication.md` (the scopes), `mcp.md`, and the public `docs/42for.net` pages (`cli/sform.md`, `platform/using-the-platform.md`, `platform/admin-ui.md`).

### 11. Tests

- **Backend.CosmosDb** (`STORYTELLER_TESTS_USE_CONTAINER=true`): per document, a restore of an old version (new version, content, `RestoredFrom`, the version list), the current version and an equal one (no new version, no audit), an unknown and an expired version (`VersionNotFound`), a deleted document brought back with the numbering continued, the cache invalidation (configurations and templates), the schema check with and without `force`; for configurations also a soft-deleted (`409`) and a purged annotation (`404`), the restore after `config delete`, the audit entry (`ConfigurationRestored`, `ConfigurationVersion`, `FromVersion`), a stored `$remove` key copied verbatim, an ordinary write after a restore without `RestoredFrom`, a concurrent change retried; for schemas the annotation states and the pair rule.
- **Api.Functions**, **sform**, **MCP** and the **admin UI** as above.

### 12. Phases

1. Models, services and Cosmos DB, with their tests.
2. API, the OpenAPI document and both SDKs.
3. `sform`, its tests and the CLI automation cases.
4. MCP tools.
5. Admin UI.

Each phase updates the docs it touches and gets its review.

### 13. Out of scope

- Restoring versions of a purged annotation (decision 2), undoing a version's expiry, and key/value versions (they have their own restore).
- An audit trail for templates and schemas: `RestoredFrom` on the version records their restores.
- `RestoredFrom` in an export: the archive keeps the versions' content and authors only.
- Preconditions (`If-Match`) on these documents.
