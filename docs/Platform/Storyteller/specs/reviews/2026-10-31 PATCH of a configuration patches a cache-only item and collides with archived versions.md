# PATCH of a configuration patches a cache-only item and collides with archived versions

Linear: [424-100](https://linear.app/424fun/issue/424-100/patch-of-a-configuration-patches-a-cache-only-item-and-collides-with).

## Overview

This review covers all of [PATCH of a configuration patches a cache-only item and collides with archived versions](../2026-10-31%20PATCH%20of%20a%20configuration%20patches%20a%20cache-only%20item%20and%20collides%20with%20archived%20versions.md), delivered in one branch.

- A `PATCH` of a configuration without a stored version answers `404`. That covers a key without an item, and also an item that only caches the effective document of a read (a cache row: version 0, no content). Before, the patch was applied to `{}` and written as the first version of a new lifetime.
- An emptied version (`{}` with version 1 or more) stays a patch target. The Admin UI, `sform` and the MCP tools patch it, for example after "Create empty".
- The version read skips an empty archive of version 0, as the version list does.
- The not-found message, the OpenAPI text, the Admin UI mock and the docs follow.

The version collision named in the issue's title was already fixed by 424-50. The issue's sequence is now a backend test: set v1, set v2, clear or delete, an effective read, a refused patch, then two writes as versions 3 and 4.

| Commit | Content |
|---|---|
| `df60bd1b` | The spec |
| `3c9dfcb2` | Sections 1 to 7: the service, the contract text, backend and API tests, MCP comments |
| `cf576c25` | Section 8: the Admin UI mock and its tests |
| `54db7d7a` | Section 9: the docs |
| `dee3508e` | Fixes from the review: "Create empty" over a cache row, and one sentence of `inheritance.md` |
| — | This review |

## What Was Done

### 1. A patch refuses a configuration without a written version

`Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`, `PatchConfigurationAsync`: the existing check after `ReadForWriteAsync` now reads `existingConfiguration is null || !IsWrittenVersion(existingConfiguration)`, as planned. It stays inside the retry loop, and before `ApplyPatch` and the no-op return.

- A soft-deleted annotation still answers `409 AnnotationDeleted`.
- A pending clear is still finished before the refusal.
- An empty or `test`-only patch of a cache row answers `404`.

No other method changed. A `PUT` over a cache row still takes the cache-row branch of `PlanArchiveAsync`.

### 2. An empty written version stays a patch target

As planned. `EmptiedVersion_IsArchived` (case `patch`) and `Patch_OfAnEmptyFirstVersion_WritesTheNextVersion` pin it.

One statement of the spec is not exact: that a `PATCH` answers `404` exactly when `GET versions/{effective Version}` does. It does not hold for data from before versions were counted. An archive of version 0 *with* content, behind a cache row, is still readable at `versions/0` (by design since 424-50, `VersionZero_WithContent_IsArchived`), while a patch of that row is refused. Before this change, the Admin UI's save failed in that state too: a `412` loop on its `test` operations, or an add-only patch that dropped the shown legacy values. Now it is a plain `404`.

### 3. An empty archive of version 0 is no version for reads either

`GetConfigurationVersionContentAsync` returns the archive only when `Version > 0 || Content.HasValues`, as `GetConfigurationVersionsAsync` filters it.

### 4. The contract text

- `Backend.Core/src/Configuring/ConfigurationNotFoundException.cs`: "Configuration for '{key}' has no stored version to patch: it was never written, or it was deleted. A PUT or POST writes a first document."
- `Backend.Core/src/Configuring/IConfigurationService.cs`: a comment on `PatchConfigurationAsync`.
- `Api.Functions/src/V1/ConfigurationHttp.cs`: the `404` description of `PatchConfiguration` reads "The configuration has no stored version to patch: it was never written, or it was deleted."

The OpenAPI shape is unchanged. The committed `open.api.v0.10.11.json` files and `ApiSdk.g.cs` keep the old description until the next export, and nothing compares the descriptions with them.

### 5. Backend tests

`Backend.CosmosDb/test/CosmosConfigurationRecoveryTests.cs`:

1. `Patch_OfACacheRow_IsRefused_AndTheNextWritesContinueAfterTheArchivedVersions` (clear, delete) runs the issue's sequence as planned. It also reads version 3 back after the fourth write.
2. `CacheRow_OfANeverWrittenConfiguration_HasNoVersion` checks the three patches in a loop inside the existing fact: an `add`, `[]`, and a `test` that fails on `{}`. After them, the row is still version 0, with no archive and no versions.
3. `EmptiedVersion_IsArchived` has the `patch` case. Its branches became a `switch`.
4. `Patch_OfAnEmptyFirstVersion_WritesTheNextVersion` became a `[Theory]` over `no-row` and `cache-row` (from the review). The Admin UI reads the effective document before "Create empty", so in practice the first `{}` is written over a cache row. The test uses the unit of the tree, which inherits from the responsibility.
5. `FailedClearInvalidation_IsCompletedByARefusedPatch` (missing, cache row) clears the unit with a failing invalidation and checks the descendant unit of execution.
6. `Patch_OfACacheRow_OfADeletedAnnotation_AnswersAnnotationDeleted`: the soft delete marks the cache row with `AnnotationDeletionId`, so `EnsureAnnotationNotDeletedAsync` answers before the check.
7. `Patch_OfARowThatBecomesACacheRow_IsRefusedOnTheRetry`: the hook clears and reads through the shared service before the patch's batch. The batch then fails on its archive (`409`) and the retry is refused.
8. `EmptyArchiveOfVersionZero_IsNoVersion` inserts the archive with `InsertHistoryAsync` from the cache row.

`Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`:

9. `PatchConfigurationAsync_WithoutAConfiguration_ThrowsNotFound` checks the message, that there are no versions, and that version 1 cannot be read. It does not read the item itself: that test class has no raw container helper, and the path creates nothing.

Against the old service (the check and the read filter reverted), six of the new or changed test cases fail:

- 5.1 (both cases);
- 5.2;
- 5.5 (cache row);
- 5.7;
- 5.8.

The others pin behaviour that this change keeps: a missing row, a soft delete, emptied and empty-first versions.

### 6. API test

`Api.Functions/test/ConfigurationPatchHttpTests.cs` (new): `PatchConfiguration_WithNothingStored_AnswersNotFoundWithTheMessage`. A `DispatchProxy` throws `ConfigurationNotFoundException` from `PatchConfigurationAsync`. The test checks the `NotFoundObjectResult`, its `ErrorResponse` message, and the key and operations that the service received. `FullKey` has no value equality, so the key is compared as text.

### 7. MCP

Comments only, as planned: `Mcp/src/Tools/ConfigurationTools.cs` and `Mcp/test/ConfigurationToolsTests.cs`.

### 8. Admin UI mock

`ui.admin/src/app/mock/configuration-handlers.ts`: the `PATCH` handler answers `404` with the API's message and no `ErrorCode` when `ownContent` is `undefined`. It still patches a stored `{}`.

`configuration-editor.test.ts`:

- The create-flow test goes on after "Create empty" with an edit and Save, which store `{ retries: 2 }`.
- The new test "gets a 404 for a patch when nothing is stored, as from the API" calls `patchConfiguration` from the SDK. It fails against the old mock.

### 9. Docs

- `inheritance.md`:
  - "Writes": a new bullet on what a patch refuses and what it patches. A cache row counts as an existing item "for a write".
  - "Archives": the version read and the empty archive of version 0.
  - "Pending invalidation": a patch that finds nothing stored.
- The review corrected the "Archives" sentence. The first wording said that a version the list leaves out cannot be read either. That is not true for an archive of a later version than the current item: only the old patch path left one behind, and it stays readable at `versions/{n}`. The sentence now says so.
- `definitions.md:165` and `:247`, as planned.
- Not in the spec: `docs/42for.net/platform/configuration.md` has a sentence that a JSON Patch sent on its own needs a stored document, and that a first document is written with `PUT` or `POST`.

### 10. Review of the branch

Three independent reviewers covered correctness, tests, and contract and clients. Each of their seven findings then went to a verifier that tried to refute it.

- **Upheld (low).** The `inheritance.md` sentence of section 9, fixed in `dee3508e`.
- **Refuted, with something taken anyway.** Two findings were about the cache-row state of "Create empty" and the test of section 5.9. The verifiers found no defect in the change, but `dee3508e` adds the cheap cache-row case of 5.4. The 5.9 limitation is noted above.
- **Refuted.** The finding that the legacy archive of version 0 with content blocks the editor described a state that only pre-versioning data can reach, and the change does not make it worse (section 2). The finding that the new 404 message suggests a `PUT` for a missing annotation was refuted too: that answer is unchanged, and the message is true for it.

### 11. Tests run

| Suite | Result |
|---|---|
| `Backend.CosmosDb.UnitTests`, local emulator | 575/575 before the review fix. After it, `CosmosConfigurationRecoveryTests` 38/38. |
| `Api.Functions.UnitTests` | 2604/2604 |
| `Mcp.UnitTests` | 727/727 |
| `Backend.Core.UnitTests` | 1106/1106 |
| Admin UI | `npm test` 1453/1453, lint, `vue-tsc` |
| Admin UI e2e, desktop and mobile | 367 passed, 8 skipped, 1 failed: the composition test of `configuration.spec.ts:320`, on `window.storytellerMock` not being ready yet. It passed 8 of 8 times alone. `configuration.spec.ts` alone: 32/32. |

## Left for later

- **`DELETE` of an emptied version** answers `404` and leaves the row (`ClearConfigurationAsync` tests the content). The Admin UI's "Delete own" therefore fails on a configuration created empty, which can now be patched.
- **The Admin UI after a `404` on save** shows a generic toast instead of switching to the create flow. It now happens after a concurrent delete, and for a cache row in front of a legacy archive of version 0 with content (section 2).
- **MCP and legacy rows.** `StoredConfigurations.GetOwnAsync` treats a version 0 with content as nothing stored.
- **Leftovers of the old patch path.** Archives of a later version than the current item stay readable. Data already written with collisions is not repaired.
- **The SDKs** keep the old `404` description until the next OpenAPI export.
