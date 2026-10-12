# Phase 2 of Soft-delete and Audit of Annotations

## Overview

Phase 2 covers the API (sections 2.1-2.9) of [Soft-delete and audit of annotations](../2026-10-11%20Soft-delete%20and%20audit%20of%20annotations.md), Linear 424-6. It builds on [Phase 1](2026-10-11%20Phase%201%20of%20Soft-delete%20and%20audit%20of%20annotations.md).

- **Endpoints.** The Functions API has six new endpoints: the deleted list, one deleted annotation, the lookup, the restore of many keys, the permanent delete and the history.
- **Changed endpoints.**
  - `DELETE …/annotations/{key}` soft-deletes and returns the deletion.
  - `SetAnnotation` returns the stored annotation.
  - The configuration endpoints tell a deleted annotation from a missing one.
- **Fixes.** `GetDescendants` follows the inheritance graph; before, its conditions never applied.
- **Version and SDKs.** The Platform version is now 0.9. `open.api.v0.9.0.json` is exported, and both SDKs are regenerated from it.

The CLI commands and the admin UI screens are Phases 3 and 4. Only what the regenerated SDKs required was changed there.

## What Was Done

### 2.1 Routes

- **`Definitions`:**
  - routes `History`, `Deleted`, `DeletedAnnotation`, `DeletedLookup` and `DeletedRestore`;
  - route ids `GetAnnotationHistory`, `GetDeletedAnnotations`, `GetDeletedAnnotation`, `FindDeletedAnnotations`, `RestoreAnnotations` and `PurgeAnnotation`, all in the `Annotations` tag;
  - parameters `roots` and `deletionId`;
  - descriptions `ResponseConcurrentChange` and `ResponseConfigurationNotFound`.
- **`AnnotationsHttp`** is now a partial class. The new functions are in `V1/AnnotationsHttp.Deleted.cs`, with two helpers:
  - `InvalidKey(key)` answers `400 InvalidKey`;
  - `TryParseKeys` rejects no keys, more than `ApiConstants.MaxInputItems` (100), or an invalid key.
- **Scopes.** Reads accept `Annotation.Read|Write` and `Default.Read|Write`. Writes accept `Annotation.Write` and `Default.Write`.
- **Route precedence.** `RouteTableTests` has two new tests:
  - `DeletedAnnotationRoutes_AreRegistered`;
  - `LiteralSegments_AreNotAnnotationKeys`, which checks that `deleted`, `lookup`, `restore` and `history` never parse as annotation keys.

### 2.2 The caller as `AuditActor`

- `HttpRequestDataExtensions.GetAuditActor()` was added in Phase 1, so the API compiled then.
- In this phase, the configuration writes pass the actor too: `Set`, `Patch` and `Delete` (through `ClearConfigurationAsync`).

### 2.3 Delete

`DeleteAnnotation` changes as follows:

- It returns `200 AnnotationDeletionResult`.
- `NotFoundException` becomes a plain `404`, and an invalid key `400`. A concurrent change is mapped to `409` by the middleware.
- The OpenAPI description states the cascade and the idempotent repeat.

### 2.4 Deleted annotations and restore

- **Service.**
  - New `IAnnotationService.GetDeletedAnnotationsAsync(organization, project, view, rootsOnly, deletionId, continuationToken)`.
  - It runs one cross-partition query over the project's partitions: `STARTSWITH(c.PartitionKey, '{project}.')`, `ProjectName`, `ViewName`, `IS_DEFINED(c.AnnotationType) AND IS_DEFINED(c.Deletion)`. `rootsOnly` and `deletionId` are optional filters.
  - Results are ordered by `c.Deletion.DeletedAt DESC`, 1000 per page (`CosmosConstants.MaxItemCountPerPage`).
  - `IS_DEFINED(c.AnnotationType)` keeps configuration and audit items out of the result.
  - The page is read by the new `AnnotationItemStore.QueryPageAsync`, which deserializes each item by its `AnnotationType`.
- The per-type filter `AnnotationsRequest.Deleted` from Phase 1 stays for the type lists. The deleted list needs all types in one ordering, so it has its own method. Next to the partition prefix the spec listed, the query also filters `c.ProjectName`.
- **Endpoints.**
  - `GET …/deleted` (`roots` defaults to `true`).
  - `GET …/deleted/{key}` (`404` when the key is not deleted).
  - `POST …/deleted/lookup` returns `Annotation[]`.
  - `POST …/deleted/restore` (Contributor) passes `Keys`, `Descendants` and the actor, and returns `AnnotationRestoreResult` with the failures per key.

### 2.5 Permanent delete

`PurgeAnnotation`:

- needs the write scopes;
- rejects a machine identity with `AccessDeniedException` (`403`) before the role check;
- requires `Administrator` (and so `Owner`) on the project;
- maps `AnnotationNotDeleted` to `409` and a missing key to `404` through the middleware.

### 2.6 History

`GetAnnotationHistory` returns `AnnotationHistory`, or `404` when the service answers `null` (a purged or unknown key).

### 2.7 Writes

- **`SetAnnotation`:**
  - Calls `SetAnnotationAsync` and returns the stored annotation, with its stamps.
  - An unparsable key answers `400 InvalidKey`.
  - Errors are mapped by `ErrorResponseMapping`: `StorytellerException` becomes `400 InvalidAnnotation` or `409 Conflict`. Other exceptions still answer `400`, as before.
  - The `TODO: [P1]` comment is gone.
- **The double read is gone.**
  - `SetAnnotationAsync` reads the annotation item alone.
  - Only when the item is active does it read the audit (`LoadDetailsAsync(node, false, true)`) and update.
  - `UpdateCoreAsync` now takes the loaded node.
  - `UpdateAnnotationAsync` loads the node and calls it.
- **Bulk writes.** `SetAnnotations` and `SetAnnotationsSimple` needed no change:
  - `CreateAnnotationsAsync` already validates every item with `AnnotationRules.ValidateAll`;
  - the middleware maps the result to `400 InvalidAnnotation`;
  - the actor was passed since Phase 1.
- **Configuration endpoints** (`ConfigurationHttp`, which now also takes `IAnnotationService`):
  - **Reads** that find nothing call `NotFoundAsync(fullKey)`. It answers `404` with `ErrorCode = AnnotationDeleted` when `GetDeletedAnnotationAsync` finds the key, and a plain `404` otherwise.
    - This covers `GetConfiguration`, the resolved configuration, one version, the versions list (when empty) and the three diffs.
    - It costs one point read, only on the not-found path. This is the approach the Phase 1 review chose (section 1.11) instead of throwing from the service.
  - **Writes.** `Set` and `Patch` map `StorytellerException`, so a deleted annotation answers `409 AnnotationDeleted`.
  - **Delete.** Without content, `Delete` answers `409 AnnotationDeleted` for a deleted annotation and `404` otherwise.
- **`GetDescendants` fix.**
  - The endpoint used to build `Condition<T>` without `Against`, so it returned every item of the requested types in the partition. This was noted in Phase 1.
  - New `AnnotationsRequest.DescendantsOf`. `GetAnnotationsAsync` applies it in three steps:
    1. It takes the partition (or partition prefix) and the key prefix of the type from `DescendantScope`.
    2. It queries with `STARTSWITH`.
    3. It filters the page in memory with `IsDescendantOf`.
  - `Count` is the filtered count. A page can hold fewer items than the page size.
  - The endpoint keeps its validation of each root type.

### 2.8 OpenAPI and SDKs

- **`maxLength`.** The OpenAPI extension emits it for `[MaxLength]` (200, 10,000 and 2048), so `AnnotationDocumentFilter` was not changed. A test covers it.
- **Responses.** The new and changed operations document their `400`, `404` and `409` responses, and `403` where it applies. The configuration reads document a `404` with an `ErrorResponse` body.
- **Version.** `src/Platform/version.json` is now `0.9-alpha`. Because the change is not committed, the build is `0.9.0.22363` and the files are named `v0.9.0`. After the commit, the version height gives `0.9.1` or later.
- **Export.**
  - Rider's `func.exe` locks `Api.Functions/src/bin`. So the API was built into a temp folder and served by a second host on port `7072`, using Rider's core tools 4.134.0 x64. `/api/openapi/v3.json` was saved, and the host was stopped.
  - `servers` was set back to `http://localhost:7071/api` to match the earlier exports.
  - The document was written to `Sdk.NSwag/open.api.v0.9.0.json` and `sdk.typescript/open.api.v0.9.0.json`.
- **Changes against `v0.8.91`:**
  - 6 new operations and 15 new schemas; nothing removed;
  - a body on `DeleteAnnotation`'s `200`;
  - `409` on `SetAnnotation` and `DeleteConfiguration`;
  - `404` bodies on the configuration reads;
  - `text/plain` on the schema diffs. This came from commit `f4606fa`, after the `v0.8.91` export.
- **`Sdk.NSwag`:**
  - `src/ApiSdk.g.cs` was regenerated with the README command. The README now names `v0.9.0`.
  - The diff is large because the previous generation predates the API phases 0a-0c for the admin UI.
  - The `ClientCredentials = 3` values added by hand in WorkOS Phase D are now generated.
- **TypeScript SDK:** `npm run generate` set the package version to `0.9.0`.
- **Fallout in `ui.admin`:**
  - The schema diff functions now return `string | DiffResult`. `app/utils/schema-targets.ts` casts them `as Promise<DiffResult>`, as the configuration diffs already did.
  - The installed SDK copy was refreshed. `npm install` changed `package-lock.json` only by dropping `libc` fields, and that was reverted.
- **Not done:** the superseded `open.api.v0.8.*.json` files are kept. The Phase A review of the TypeScript SDK suggests deleting them once 0.9 is exported.

### 2.9 Tests

| Project | Result |
| --- | --- |
| `Api.Functions/test` | 177 pass (146 before). `AnnotationsHttpTests` was rewritten around a recording `IAnnotationService` double. It covers: <ul><li>set: the stored result, an invalid key, `400 InvalidAnnotation`, and bodies without the type members or with an unknown type;</li><li>delete, including `404`;</li><li>the descendants request;</li><li>the deleted list and the `404` of a key that is not deleted;</li><li>lookup limits and invalid keys;</li><li>restore with the keys, the flag and the caller;</li><li>purge by a machine (`403`) and by a user;</li><li>history `404`.</li></ul> **Deviation:** the spec's purge role matrix (Contributor `403`, Administrator and Owner `200`) is not tested at the HTTP level, because the test run uses a Debug build, where `DEV_AUTH` skips the role checks. The machine rule runs before the role check, so it is tested. Also new: <ul><li>`RouteTableTests` +2;</li><li>`OpenApiDocumentTests.AnnotationSchema_PublishesTheMaximalLengths`;</li><li>`ErrorResponseMappingTests` cases for `InvalidAnnotation`, `InvalidKey`, `AnnotationDeleted` and `AnnotationNotDeleted`.</li></ul> |
| `Backend.CosmosDb/test` | 260 pass (258 before), against the Cosmos emulator. New tests: <ul><li>`DeletedListing_IsNewestFirstAndFiltersRootsAndDeletions`;</li><li>`Descendants_FollowTheInheritanceGraph`: a context's executions and their units of execution, a unit's units of execution across executions, and a deleted execution left out.</li></ul> |
| `Backend.Core/test` | 34 pass |
| `Cli/test` (on the regenerated `Sdk.NSwag`) | 75 pass |
| `sdk.typescript` | typecheck, lint, 59 pass (1 skipped), build |
| `ui.admin` | build (`vue-tsc` and Vite), lint, 157 pass. **Not run:** `npm run test:e2e`, because Playwright's Chromium (build 1248) is not installed on this machine. |

`Api.Web`, `DbCreator` and `Binding.Core` build.

### Documentation

- **[annotation-lifecycle.md](../../annotation-lifecycle.md):**
  - new section **API**: the route table, route precedence, the descendants rule, configuration answers for a deleted annotation, and the client flow for creating;
  - error code `InvalidKey`.
- **[access.md](../../access.md):** the roles for reading deleted annotations and history, for delete, restore and re-create, and for the permanent delete (Administrator, users only).
- **[using-the-platform.md](../../../../42for.net/platform/using-the-platform.md)** (public site): delete and restore, the deleted routes, and re-creating a deleted key.

### Notes

- **Name queries on the list endpoints don't filter (bug from before this phase).** The list endpoints (`GetResponsibilities`, `GetSubjects`, `GetUsages`, `GetContexts`, `GetExecutions`, `GetUnits`, `GetUnitsOfExecution`) build `AnnotationsRequest.Condition<T>` without `Against`, and they cast model types to entity types. So `nameQuery` never filters. This phase does not fix it.
- **Restart the local host.** The host Rider runs still serves the previous build until it is restarted.

### Follow-up of 2026-10-12: review findings

**Fixed:**

- **`BatchPlan`** limits a batch by payload as well as by operations: at most about 1.5 MB, below the 2 MB request limit of a transactional batch. Every unit is added with an estimate of its payload: the annotation and audit streams, plus the configuration snapshot (its content and a 1 KB allowance). A unit is never split, so a unit above the limit gets a batch of its own.
- **Bulk create (`CreateAnnotationsAsync`)** skips a key on `ConflictException` only when the annotation is now active. When retries ran out under contention, the exception reaches the caller instead of the key being dropped silently.
- **Restore** deletes the configuration snapshot of a deletion only when it still exists. This is checked with `AnnotationItemStore.ExistsAsync`, a point read. Before, an absent snapshot failed the whole batch, and the key could not be restored. New test: `Restore_WithoutTheDeletionSnapshot_StillRestoresTheConfiguration`.
- **Delete** treats a deleted descendant as part of an unfinished delete only when it was deleted after the root last became active. The root's `Modified`, or else `Created`, marks that time. Before, a delete after a restore without descendants reused the old `DeletionId` and `DeletedAt`, and so a shorter retention. New test: `Delete_AfterARestoreWithoutDescendants_StartsANewDeletion`.
- **DbCreator seed:** `Create.UnitOfExecution` builds the key `uxe.{subject}.{responsibility}.{context}.{unit}`, the unit key `unt.{responsibility}.{unit}`, and the name from the unit.
- **Lookup and restore** answer `ErrorCode = InvalidKey` for no keys and too many keys too.
- **OpenAPI:**
  - `SetAnnotations` and `SetAnnotationsSimple` document their real `200` body, the created annotations. Before, it was a `Guid`.
  - `SetAnnotationsSimple` documents its real request body, a list of strings.
  - `AnnotationRestoreRequest.Descendants` has the default `true`, set by `AnnotationDocumentFilter`, so generated clients keep the API's default.
  - `open.api.v0.9.0.json` was exported again, and both SDKs were regenerated.

**Skipped:**

- **Audit entry in the same batch as the configuration write.** The spec (1.9, "Configuration events") decided on a separate, best-effort append after the write. A failed append must not fail a configuration write, and the audit's ETag must not make configuration writes contend with annotation writes.
- **History TTL when container TTL is enabled.** Version items already carry a 365-day `ttl`, and their expiry is the decided behavior (decision 14). No real data exists yet.
- **`SetAnnotationsSimple` writes to `main/default` whatever the route says.** Fixed afterwards; see the next section.

**Tests after the follow-up:** `Backend.CosmosDb` 262, `Api.Functions` 177, CLI 75, TypeScript SDK 59, `ui.admin` 157. All pass.

### Follow-up of 2026-10-12: simple bulk create in the route's project and view

- **Before:** `SetAnnotationsSimple` (`POST …/annotations/simple`) checked `Contributor` on the route's project, but created the annotations in `main` / `default`. So a Contributor of any project could write into `main`.
- **Change:** `IAnnotationService.CreateAnnotationsFromStringAsync` takes `project` and `view`, and `AnnotationFactory.CreateDefault` uses them. The endpoint passes the route's values, so the write goes to the project the access check covered. Callers that relied on the old behavior must now call the `main` / `default` route.
- **OpenAPI:** unchanged, because the route and the bodies stay the same, so the SDKs need no regeneration.
- **Tests:**
  - New: `CreateAnnotationsFromString_UsesTheGivenProjectAndView` (Cosmos) and `SetSimple_CreatesInTheProjectAndViewOfTheRoute` (API).
  - The existing string-create test calls the `main` / `default` overload of `AnnotationServiceTestExtensions`.
  - `Backend.CosmosDb` 263 and `Api.Functions` 178 pass.
- **Docs:** the public [using-the-platform.md](../../../../42for.net/platform/using-the-platform.md) no longer says that compact lines go to `main` / `default`.
