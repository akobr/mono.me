# PATCH of a configuration patches a cache-only item and collides with archived versions

Linear: [424-100](https://linear.app/424fun/issue/424-100/patch-of-a-configuration-patches-a-cache-only-item-and-collides-with). Found while building the MCP server (424-84, review `reviews/2026-10-24 MCP Server for the Storyteller API.md`, "Left for later").

## Problem

`PatchConfigurationAsync` refuses only a configuration that has no item. An effective read of an annotation that inherits values creates a **cache row**: an item with `Version 0`, empty `Content` and the calculated document. A `PATCH` treats that row as a stored document. The issue was written on 2026-10-25, when the patch then archived an empty version 0 and wrote version 1. After a `DELETE` had kept versions 1..N, that version 1 collided with the archive of the earlier version 1.

424-50 (spec `2026-10-26 Failure-Safe and Race-Free Configuration Cache Invalidation.md`) moved the patch onto the shared `TryWriteNextVersionAsync` and `PlanArchiveAsync`. Since then a patch of a cache row archives nothing and numbers its version after the highest archived one, so the collision is gone. What is still wrong:

1. **A patch of a cache row writes a version.** The operations are applied to `{}`, and the result becomes the first version of a new lifetime, although nothing was stored. The OpenAPI text promises `404` ("doesn't exist or has no content to patch"). The Admin UI and the MCP tools say in their comments that such a patch fails, so they never send it. The issue's sequence (set v1, set v2, delete, effective read, patch) answers `200` with version 3 instead of `404`.
2. **The answer depends on an earlier read.** On the same key, the same patch answers `404` before any read and `200` after an effective read has cached the document.
3. **Patches that change nothing look like a version 0.** An empty patch, or one with only `test` operations that pass on `{}`, takes the no-op return and answers `200` with the cache row as a `Configuration` of `Version 0`. A failing `test` answers `412` instead of `404`.
4. **Retries can start from a partial document.** When a concurrent `DELETE` and a read turn the row into a cache row between two attempts, the next attempt applies the operations to `{}`.
5. **Leftovers of the old patch path are served as versions.** The old path wrote empty archives of version 0 (`{view}.cfv.{key}.0` with `{}`). `GetConfigurationVersionsAsync` hides them, but `GetConfigurationVersionContentAsync` still returns one when the current row is a cache row. The Admin UI reads `versions/0` for an annotation without a stored version and expects a `404`. With such a leftover it gets `200 {}` and opens the editor instead of the create flow. Once item 1 is fixed, its save would answer `404`.

No backend test covers the patch of a missing item. The one test of a cache-row patch, `Patch_OfACacheRow_ContinuesAfterTheArchivedVersions`, asserts the current behaviour.

### Acceptance criteria

| Expectation of the issue | Where |
| --- | --- |
| PATCH answers `404` when nothing is stored | Section 1. The condition is "no written version" (a missing item or a cache row), not "empty `Content`". Section 2 explains why. |
| Version numbers continue after the highest archived version, and empty content is not archived | Already done by 424-50. Section 1 keeps it: a patch never reaches `PlanArchiveAsync` with a cache row any more, and the next write still continues after the archives. Tested in section 5. |
| A backend test for the issue's sequence | Section 5.1 |

The inputs to this spec are:

- the issue, its relation 424-84 and the code at `main` `08afd3d9`;
- the 424-50 spec and its review (`reviews/2026-10-26 Phase 1 of Failure-Safe and Race-Free Configuration Cache Invalidation.md`), and commit `efbc584e7`;
- three independent read-only reviews of the open question in section 2, through three lenses: client compatibility, contract consistency, and data and versioning invariants. All three chose the same condition. Their findings are in sections 3 to 8;
- the CodeRabbit coding plan attached to the issue (`2026-10-31 PATCH of a configuration patches collides.md`). It is used as a second opinion and a source of ideas, not as the decision. Section 10 lists where this spec differs from it.

## Current State

Paths are relative to `src/Platform/Storyteller/` unless they start with `docs/` or `src/`.

### The service

`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`:

- `PatchConfigurationAsync` (`:491-533`) runs up to `MaxRetries + 1` attempts. Each attempt does the following, in order:
  1. `ReadForWriteAsync` (`:500`, `:1362-1375`) reads the row with its ETag.
  2. `EnsureAnnotationNotDeletedAsync` (`:1594-1606`) throws `409 AnnotationDeleted` for a soft-deleted annotation.
  3. `CompletePendingWorkAsync` (`:1345-1358`) finishes the row's pending invalidation, and the key's pending clears when the row is missing or has no content. If it did anything, the row is read again.
  4. Only a missing row throws `ConfigurationNotFoundException` (`:502-505`).
  5. `ApplyPatch` (`:508`).
  6. The no-op return when the content is unchanged (`:510-513`).
  7. Schema validation (`:515-518`).
  8. `TryWriteNextVersionAsync` (`:520`).
- `IsWrittenVersion` (`:1196-1201`): `Version > 0 || Content.HasValues`. A cache row is the one row that is not a written version (decision 8 of the 424-50 spec).
- `PlanArchiveAsync` (`:1152-1181`): a cache row has no archive, and the next version is the highest archived version + 1. `WriteConfigurationAsync` (`:389-489`) uses the same rule: a `PUT` over a cache row writes the first version of a new lifetime.
- `ClearConfigurationAsync` (`:535-567`) returns `false` for a row without content, so the `DELETE` route answers `404` for an emptied version too (decision 6 of the 424-50 spec).
- `GetConfigurationVersionsAsync` (`:304-318`) skips empty archives of version 0 and the cache row. `GetConfigurationVersionContentAsync` (`:321-356`) prefers the current row when it is a written version of the requested number. Otherwise it reads the archive, without the listing's filter.

### History of the condition

- `7b272f588` and `397d4ef78` (2026-05-03, JSON Patch support): `existingConfiguration is null || !existingConfiguration.Content.HasValues`.
- `efbc584e7` (2026-05-08, "fix: make empty configuration a valid patch target"): the owner removed `|| !Content.HasValues`, so that an empty document can be patched. At that time a cache row was not a separate concept, so the change also let cache rows through.
- 424-50 (2026-10-26) defined the written version, and with it the cache row, and made an emptied version a version everywhere. An emptied version is a written version with `{}`: it is listed, readable at `versions/{n}`, archived on the next write, and shown as `Empty` in the composition. A cache row shows as `Missing` (`inheritance.md:274`).

### The contract text

- `Backend.Core/src/Configuring/ConfigurationNotFoundException.cs`: "Configuration for '{key}' does not exist or has no content to patch." Only the patch throws it. `sform config patch` prints the message (`Cli/src/Commands/Configuration/ConfigPatchCommand.cs:179-194`).
- `Api.Functions/src/V1/ConfigurationHttp.cs:362`: the `404` of `PatchConfiguration` reads "The configuration doesn't exist or has no content to patch." `:415-418` maps the exception to `404` with an `ErrorResponse`. No test covers that mapping.
- `Backend.Core/src/Configuring/IConfigurationService.cs:49` has no comment on `PatchConfigurationAsync`.

### The clients

Every first-party client decides between `PATCH` and a first write by asking whether a written version exists:

- **Admin UI.** `ui.admin/src/app/composables/useConfiguration.ts:27-38` reads `own = GET versions/{effective.Version}`. A `404` sets `noOwn`, and `ConfigurationEditor.vue:249-266` then offers "Create empty" and "Start from template", which `POST` (`:201`). Otherwise `save()` always sends a `PATCH` (`:122`). "Create empty" stores version 1 with `{}`, the editor loads `{}`, and the next save patches it.
- **sform.** `Cli/src/Services/StorytellerApiExtensions.cs:32-51` (`GetStoredConfigurationAsync`) looks for the current entry in `/versions`. Without one, `ReplaceConfigurationAsync` (`:68-88`) sends a `PUT`; with one, it sends a `PATCH` computed from the stored content, also from `{}`.
- **MCP.** `Mcp/src/Tools/StoredConfigurations.cs:20-26` treats `Version <= 0` as nothing stored. `patch_configuration` refuses that state (`ConfigurationTools.cs:189-198`), and `replace_configuration` and `revert_configuration` send a `PATCH` when a version is stored (`:277-293`). The comment at `:190-191` describes the collision that 424-50 fixed.
- **Admin UI mock.** `ui.admin/src/app/mock/configuration-handlers.ts:409-456` patches `ownContent(...) ?? {}`. Unlike the API, it therefore creates a version when nothing is stored. The mock has no cache rows.

### Tests

`Backend.CosmosDb/test/CosmosConfigurationRecoveryTests.cs`:

- `Patch_OfACacheRow_ContinuesAfterTheArchivedVersions` (`:411-432`) expects the cache-row patch to store version 3.
- `CacheRow_OfANeverWrittenConfiguration_HasNoVersion` (`:434-447`), `EmptiedVersion_IsArchived` (`:449-480`) and `FailedClearInvalidation_IsCompletedByARepeatedClear` (`:247-273`) are the closest neighbours.
- `CreateTreeAsync`, `ReadRowAsync`, `HasItemAsync`, `HistoryId`, `InsertHistoryAsync` and `CreateService` (an `InterceptedRepositoryProvider` with `BeforeBatch` hooks) are the helpers.

Elsewhere:

- `CosmosConfigurationServiceTests.cs:849-978` holds the `PatchConfigurationAsync_*` tests. All of them patch a written version with content.
- Every other test that patches a configuration patches a written version. Of those, `CosmosListingTests.cs:38` and `EmptiedVersion_IsArchived` empty the version, and `CosmosAnnotationLifecycleTests.cs:468` patches a soft-deleted annotation.
- `Mcp/test/ConfigurationToolsTests.cs:155-169`: the comment at `:158` says "the API would patch it as stored".
- `ui.admin/src/test/unit/configuration/configuration-editor.test.ts:67-81` ends after "Create empty" with own `{}`.

### Docs

- `docs/Platform/Storyteller/inheritance.md:209-218` ("Writes", "Archives", "Pending invalidation") does not say what a patch does with a missing item or a cache row.
- `docs/Platform/Storyteller/definitions.md:165` and `:247` say "a patch of a missing configuration".
- `docs/Platform/Storyteller/mcp.md:310` already says that `patch_configuration` is refused when nothing is stored.

## Proposed Changes

### 1. A patch refuses a configuration without a written version

In `PatchConfigurationAsync`, the check after `ReadForWriteAsync` becomes:

```csharp
// a cache row (version 0, no content) only caches the effective document of a read: nothing is stored to patch
if (existingConfiguration is null || !IsWrittenVersion(existingConfiguration))
{
    throw new ConfigurationNotFoundException(key.Annotation);
}
```

- **Inside the loop, right after the read.** `EnsureAnnotationNotDeletedAsync` runs first, so `409 AnnotationDeleted` still wins over `404`. `CompletePendingWorkAsync` runs first too: a cache row has no content, so the key's pending clears are finished, and a refused patch still finishes what an earlier request left pending (decision 7 of the 424-50 spec). Every attempt checks its own read, so a row that becomes a cache row between attempts is refused.
- **Before `ApplyPatch` and the no-op return.** An empty or a `test`-only patch of a cache row answers `404`, not `200` with version 0, and a failing `test` answers `404`, not `412`.
- **Unchanged:** `ApplyPatch`, the no-op return, the validation, `TryWriteNextVersionAsync`, `PlanArchiveAsync` (its cache-row branch still serves a `PUT` over a cache row), `WriteConfigurationAsync`, `ClearConfigurationAsync` and `DeleteAsync`. No version arithmetic is added to the patch.
- **The issue's sequence.** Set v1, set v2, delete, an effective read, then a patch answers `404` and writes nothing. `versions/1` and `versions/2` keep their content. The next write stores version 3, and the one after it version 4.

### 2. An empty written version stays a patch target

The issue asks for `404` "when there is no stored content (`!Content.HasValues`)". The CodeRabbit plan takes that literally, and refuses an emptied version (`Version > 0`, `{}`) too. This spec does not:

- **The owner removed that condition on purpose.** `efbc584e7` dropped exactly `|| !Content.HasValues` ("make empty configuration a valid patch target"). The new check takes back only what that change let through by accident, the cache row.
- **An emptied version is a version.** Decision 8 makes it one everywhere: the version list, the version read, the no-op rule of writes, the archive, and the composition (`Empty`, not `Missing`). If `PATCH` alone answered "not found" for a document that `GET versions/{n}` returns with `200`, it would contradict all of these.
- **The clients patch it.** Under "empty content" all of these flows would end in a `404` with no way out:
  - the Admin UI's "Create empty" followed by an edit and Save. The create buttons do not come back, because `noOwn` stays false;
  - an edit and Save after a save that removed every property;
  - `sform config edit`, `config set --replace` and `config patch` after the document was emptied;
  - MCP `replace_configuration`, `revert_configuration` and `patch_configuration` after a replace with `{}`.
- **HTTP and JSON Patch semantics.** RFC 5789 keeps `404` for a resource that does not exist. An emptied configuration exists. RFC 6902 applies to `{}` like to any document. An `add` works, and a `remove` of a missing path is a patch error (`400`), not a missing resource.
- **The issue's own reference.** It cites "the MCP `patch_configuration` already refuses when nothing is stored". The MCP's test is `Version <= 0`, which is this check (section 1).

With section 3, a `PATCH` answers `404` exactly when `GET versions/{effective Version}` answers `404`. All three clients already use that test to choose between `PATCH` and a first write.

One asymmetry stays: `DELETE` of an emptied version answers `404`, because `ClearConfigurationAsync` tests the content (decision 6 of the 424-50 spec). Section 9 leaves it for later.

### 3. An empty archive of version 0 is no version for reads either

`GetConfigurationVersionContentAsync` applies the listing's filter to the archive it reads:

```csharp
// an empty archive of version 0 holds no written version (an old patch archived cache rows so), as in the version list
return versionEntity is not null && (versionEntity.Version > 0 || versionEntity.Content.HasValues)
    ? versionEntity.ToConfiguration()
    : null;
```

The version list and the version read then agree. For a key whose current row is a cache row, `versions/0` answers `404` even when an old patch left an empty archive behind, so the Admin UI offers the create flow, and its save never runs into section 1. `VersionZero_WithContent_IsArchived` keeps passing, because its archive of version 0 has content. `GetConfigurationVersionChangesAsync` reads version 0 as `{}` without asking the storage, so it does not change.

### 4. The contract text

- `ConfigurationNotFoundException`: "Configuration for '{key}' has no stored version to patch: it was never written, or it was deleted. A PUT or POST writes a first document." `sform` prints this message to the user.
- `IConfigurationService.PatchConfigurationAsync` gets a comment: it throws `ConfigurationNotFoundException` when no version is stored (no item, or only the cache of an effective read), and it patches every written version, an empty one too.
- The `404` description of `PatchConfiguration` becomes "The configuration has no stored version to patch: it was never written, or it was deleted." Only the description changes. The status codes and the body types stay, so the shape of the OpenAPI document and the SDKs do not change. The committed `open.api.v0.10.11.json` files and the generated `ApiSdk.g.cs` keep the old text until the next export, as 424-50 did with its `409` descriptions.

### 5. Backend tests

`Backend.CosmosDb/test/CosmosConfigurationRecoveryTests.cs`:

1. `Patch_OfACacheRow_ContinuesAfterTheArchivedVersions` becomes `Patch_OfACacheRow_IsRefused_AndTheNextWritesContinueAfterTheArchivedVersions`, a `[Theory]` over `clear` (`ClearConfigurationAsync`, the HTTP `DELETE`) and `delete` (`DeleteAsync`), with one project per case. The issue's sequence:
   1. The responsibility gets `{ "a": 1 }`, and the unit gets v1 `{ "u": 1 }` and v2 `{ "u": 2 }`.
   2. The unit is cleared or deleted.
   3. An effective read of the unit returns the inherited `a = 1`. The row is a cache row (version 0), and the versions are `[1, 2]`.
   4. The patch throws `ConfigurationNotFoundException`. Afterwards the row is still version 0 with empty content, there is no archive of version 0, the versions are `[1, 2]`, and `versions/1` and `versions/2` read `u = 1` and `u = 2`.
   5. A write of `{ "u": 3 }` stores version 3, and the versions are `[1, 2, 3]`. `versions/1` still reads `u = 1`.
   6. A write of `{ "u": 4 }` stores version 4. This is the write that failed with a Cosmos `409` in the issue.
2. `CacheRow_OfANeverWrittenConfiguration_HasNoVersion` additionally checks three patches of the cache row: an `add` patch, an empty patch, and a patch whose `test` fails on `{}`. Each throws `ConfigurationNotFoundException`, and afterwards the row is still version 0, there is no archive of version 0, and there are no versions.
3. `EmptiedVersion_IsArchived` gets a `patch` case: an `add /b` patch of the emptied version 2 stores version 3. Version 2 is archived, the versions are `[1, 2, 3]`, and the diff of version 3 works. This pins section 2.
4. `Patch_OfAnEmptyFirstVersion_WritesTheNextVersion`: a `PUT` of `{}` on a new key stores version 1, and a patch then stores version 2 (the Admin UI's "Create empty" followed by Save).
5. `FailedClearInvalidation_IsCompletedByARefusedPatch`, a `[Theory]` over `missing` (no read after the clear) and `cache row` (an effective read after the clear). The unit's clear fails its invalidation, and the descendant unit of execution keeps its cache. The patch throws `ConfigurationNotFoundException`, and still clears the descendant's cache and the archive's token.
6. `Patch_OfACacheRow_OfADeletedAnnotation_AnswersAnnotationDeleted`: a cache row of an annotation that is then soft-deleted. The patch throws `ConflictException` with `AnnotationDeleted`, not `ConfigurationNotFoundException`.
7. `Patch_OfARowThatBecomesACacheRow_IsRefusedOnTheRetry`: a `BeforeBatch` hook on the patch's write clears the unit and reads it, so a cache row replaces the row the patch read. The guarded write loses, and the retry throws `ConfigurationNotFoundException`. Afterwards the versions are `[1]`, version 1 reads its old content, and the row is version 0.
8. `EmptyArchiveOfVersionZero_IsNoVersion`: with a cache row and an empty archive of version 0 inserted the way the old patch path wrote it, `GetConfigurationVersionContentAsync(key, 0)` returns `null`, and the version list stays empty (section 3).

`Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`:

9. `PatchConfigurationAsync_WithoutAConfiguration_ThrowsNotFound`, next to the other `PatchConfigurationAsync_*` tests: an annotation without any configuration item. The patch throws `ConfigurationNotFoundException`, and no item is created.

### 6. API test

`Api.Functions/test/ConfigurationPatchHttpTests.cs` (new): `PatchConfiguration` maps a `ConfigurationNotFoundException` from the service to `404` with an `ErrorResponse` that carries the exception's message. The service is a `DispatchProxy` that throws from `PatchConfigurationAsync`, as the other handler tests build their doubles. The HTTP route keeps no check of its own: the service decides, inside its retry loop.

### 7. MCP

Comments only. `Mcp/src/Tools/ConfigurationTools.cs:190-191` says that the API refuses a patch when nothing is stored, and that the tool checks first so that it can name the first writes. `Mcp/test/ConfigurationToolsTests.cs:158` gets the same wording. The behaviour stays: the tool's test (`Version <= 0`) is the API's condition.

### 8. Admin UI mock

`ui.admin/src/app/mock/configuration-handlers.ts`, the `PATCH` handler: when `ownContent(db, scope, key)` is `undefined`, it answers `404` with the API's message and no `ErrorCode`, and stores nothing. It still patches a stored `{}`. The mock has no cache rows, so "no stored document" is the condition of section 1.

`ui.admin/src/test/unit/configuration/configuration-editor.test.ts`:

- "offers the create flow when nothing is stored yet" goes on after "Create empty": an edit and Save store version 2 through a patch. This pins the flow of section 2 in the UI.
- A new case: a `patchConfiguration` of a key without a stored configuration answers `404`, and nothing is stored.

### 9. Docs

- `docs/Platform/Storyteller/inheritance.md`:
  - "Writes": `PatchConfigurationAsync` refuses a missing item and a system cache row with `ConfigurationNotFoundException` (`404`), after it has finished the pending work. It patches every written version, an empty one too. "A system cache row counts as an existing item" applies to the first insert of a write.
  - "Archives": the version read skips an empty archive of version 0, as the version list does.
  - "Pending invalidation": the requests that finish it include a patch that finds nothing stored (`404`).
- `docs/Platform/Storyteller/definitions.md:165` and `:247`: a missing configuration, for a patch, is one without a stored version. An emptied document is still a stored version.

Left as they are: `mcp.md:310` (already correct) and the specs and reviews of earlier changes.

### 10. Differences from the CodeRabbit plan

- **The condition.** The plan refuses `!Content.HasValues`. This spec refuses `!IsWrittenVersion` (section 2). For the same reason, the plan's extension of `EmptiedVersion_IsArchived` (a refused patch of the emptied version 2) is reversed here into section 5.3, which expects version 3.
- **The check comes before the no-op return.** The plan keeps the no-op return as it is and puts its check before it, but it does not name the empty and `test`-only patches of a cache row. Section 5.2 tests them.
- **Added:** the version read of section 3, the new wording of section 4 (the plan leaves the message and the OpenAPI text as they are, but they would describe the rejected condition), the API test of section 6, the MCP comments of section 7, the mock of section 8, and the tests 5.5 to 5.8 (recovery, soft delete, retry, the empty archive of version 0).
- **The same as in the plan:** the check sits inside the retry loop after the read and keeps the soft-delete order; the issue's sequence is a theory over clear and delete; the shared version and archive helpers do not change; there is no HTTP patch infrastructure beyond one handler test.

### 11. Left out

- **`DELETE` of an emptied version** answers `404` and leaves the row, so the Admin UI's "Delete own" fails on a configuration created empty. `ClearConfigurationAsync` tests the content by decision 6 of the 424-50 spec. Aligning it with `IsWrittenVersion` (archive the `{}` and delete the row) is a separate change.
- **MCP and legacy rows.** `StoredConfigurations.GetOwnAsync` treats a version 0 with content, from before versions were counted, as nothing stored. For such a row the MCP refuses a patch the API would accept, and its replace becomes a merge.
- **The Admin UI after a `404` on save** shows a generic toast. It does not switch to the create flow. After this change, only a concurrent delete leads there.
- Data that the old patch path already wrote with collisions, and the content-based lifecycle snapshots and `DocumentHistories` (out of scope in 424-50 too).
- Regenerating the SDKs for the new `404` description.

## Delivery

One branch, `fix/424-100`, in these commits: this spec; sections 1 to 7 (service, contract text, backend and API tests, MCP comments); section 8 (Admin UI mock and its tests); section 9 (docs); and the review.
