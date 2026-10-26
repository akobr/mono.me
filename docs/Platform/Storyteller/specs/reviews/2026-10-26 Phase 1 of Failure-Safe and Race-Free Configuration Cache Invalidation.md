# Phase 1 of Failure-Safe and Race-Free Configuration Cache Invalidation

## Overview

Every configuration write, patch, import, clear and delete now reads its row with the ETag and archives the stored version without overwriting history. It writes under that ETag, retries a lost race up to three times, commits, audits, and only then invalidates the descendants. A token on the row, or on the archive of a clear, marks an invalidation that has not finished. The next write request on the same configuration finishes it, also when that request fails or finds nothing to clear. A failed invalidation therefore no longer wedges a configuration (problem 1 of the spec), and the update and patch paths no longer invalidate before they write (problem 2). This review covers phase 1 of [Failure-Safe and Race-Free Configuration Cache Invalidation](../2026-10-26%20Failure-Safe%20and%20Race-Free%20Configuration%20Cache%20Invalidation.md), commit `ac8e7678`, and the phase-1 part of the code-review fixes in the follow-up commit. Linear: [424-50](https://linear.app/424fun/issue/424-50/make-configuration-cache-invalidation-failure-safe-and-race-free).

The spec was reviewed adversarially twice before it was committed. Its decisions already include those findings: the archive reads the stored version first, the marker is a token rather than a flag filtered on the version, every attempt finishes pending work first, and a cache row is version 0 without content.

## What Was Done

### 1.1 Entities

- `ConfigurationEntity.PendingInvalidationId` and `ConfigurationHistoryEntity.PendingInvalidationId` (`string?`, not written when null).
- `PendingInvalidationRow` in `Configuring/ListingRows.cs` projects `id`, the token and `Version` for the pending-clears query.

### 1.2 Batch execution and retries

In `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`:

- `MaxRetries = 3` and `MaxBatchPayloadBytes = 1_500_000`.
- `TryExecuteWriteAsync`: `409`, `412` and `404` ask for a retry. Any other failure is logged and thrown as `ConfigurationStorageException`. `TryWriteItemAsync` classifies single writes the same way.
- `ConcurrentChange` builds the `ConflictException` with `ErrorCodes.Conflict` that every write throws when its retries run out. The view import already reports that per file.
- `ReadForWriteAsync`: read with the ETag, `EnsureAnnotationNotDeletedAsync`, finish pending work, and read again when something was finished. The re-read comes from the second plan review; without it, finishing a token always cost the attempt its guarded write.
- `ToHistory`, `GetConfigurationId`, `GetHistoryId` and `NewInvalidationId` replace the four copies of the history construction.

### 1.3 The archive

`PlanArchiveAsync` returns an `ArchivePlan(Version?, IsStored, NextVersion)`:

- a cache row (`IsWrittenVersion` is false) has no archive, and its next version follows the highest archived one;
- a missing `cfv.{V}` is created at `V`;
- an equal one (same `Content` and `Modified`) counts as archived;
- a colliding one moves the archive to the highest archived version *H*, or counts as archived at *H* when an earlier attempt already put an equal copy there.

The next version after a version 0 with content also follows the highest archived one.

### 1.4 Writing the next version

`TryWriteNextVersionAsync` writes the archive's `CreateItem` and the guarded `ReplaceItem` in one batch. When the two documents together exceed the batch margin, it writes the archive with `CreateItemAsync` first and then the guarded `ReplaceItemAsync`. The new version carries a token, except for a unit of execution.

### 1.5 Create and update

`WriteConfigurationAsync` is a loop of four attempts, each on a copy of the input.

- **Create branch:** a batch `CreateItem`. A `409` (a reader created a cache row meanwhile) retries into the update branch, where the old code answered `500`.
- **Update branch:** the no-op check follows decision 8. A document equal to a written version's content writes nothing, `{}` over an emptied version included. Over a cache row, any document is the first one.

### 1.6 Patch

`PatchConfigurationAsync` uses the same loop. It applies the JSON Patch to the content of every attempt and keeps `412 PatchTestFailed`. A missing row throws `ConfigurationNotFoundException` after finishing the pending clears. The comment that the upsert could not be guarded is gone.

### 1.7 Import

`ImportConfigurationAsync` finishes the row's pending work on `Unchanged` and on `Skipped` with `DocumentExists`, outside a dry run. A failed invalidation still stops the job, as for templates.

### 1.8 Finishing pending work

- `CompleteInvalidationAsync` invalidates and then removes the row token. The removal is a `Remove` patch filtered on the token and on `NOT IS_DEFINED(c.AnnotationDeletionId)`.
- `CompletePendingWorkAsync` finishes the row token, and the pending clears of a missing or empty row, with a single invalidation.
- `CompletePendingClearsAsync` collects the tokens before it invalidates: the queried ones and the one the clear just wrote, passed explicitly so they do not depend on the query seeing the write.
- `RemovePendingInvalidationAsync` treats `412` and `404` as expected and logs anything else.

A unit of execution has no descendants. Its writes set no token, and after the code-review fix its clears and deletes do not either. `ReadPendingClearsAsync` skips the query for it.

### 1.9 Clear and delete

- `ClearConfigurationAsync` returns `Task<bool>` (`IConfigurationService`, `TransferFakes`). One batch archives with the token, or patches the token onto an equal archive, together with the guarded `DeleteItem`. The audit uses the archived version.
- `DeleteAsync` → `ForceDeleteConfigurationAsync` retries the same way. It returns the deleted row and its archive. A cache row is deleted with an ETag-guarded `DeleteItemStreamAsync`. `DeleteAsync` then invalidates unconditionally and removes the tokens.
- `ConfigurationHttp.DeleteConfiguration` calls the clear directly: `200`, `404`, or the mapped `StorytellerException`. `MachineRestrictionHttpTests` returns `true` from the clear.

### 1.10 Version listing and content

- `GetConfigurationVersionsAsync` hides empty archives of version 0 and, for a written current row, archives of its version or later.
- `GetConfigurationVersionContentAsync` prefers the current row when it is a written version of the requested number. The code review narrowed this from "any current row". Otherwise it reads the archive, and only then falls back to a matching cache row. A cache row would otherwise have hidden an archived version 0.

### 1.11 HTTP descriptions

The `409` descriptions of `SetConfiguration`, `PatchConfiguration` and `DeleteConfiguration` name the concurrency conflict. The SDKs were not regenerated, because the OpenAPI shape is unchanged.

### 1.12 Test seam

- `Startup` reads `STORYTELLER_TESTS_USE_CONTAINER` and rewrites the Testcontainers connection string to `https`.
- `InterceptedContainer` (all 46 members), `InterceptedBatch`, `StorageInterceptor`, `RecordedBatch`, `RecordedOperation`, `BatchOperationKind`, `FailedBatchResponse` and `InterceptedRepositoryProvider` were added. No package was added.
- With `TESTCONTAINERS_REUSE_ENABLE=true` and `TESTCONTAINERS_RYUK_DISABLED=true`, the emulator container stays up between runs. Without them, each run restarted it, and the first tests of a run failed while it booted.

### 1.13 Tests

`CosmosConfigurationRecoveryTests`, one project per test:

- `FailedInvalidation_CommitsTheWrite_AndARepeatedRequestCompletesIt` (update, patch, create, import)
- `FailedInvalidation_IsCompletedByARequestThatFails`
- `FailedInvalidation_IsCompletedByADifferentWrite_InOneAttempt`
- `OrphanHistory_OfTheCurrentVersion_CountsAsArchived` (write, patch, import, clear, delete)
- `CollidingHistory_IsNeverOverwritten`
- `CollidingHistory_ArchivedByAnEarlierAttempt_IsNotArchivedAgain`
- `FailedClearOrDelete_Throws_AndKeepsTheDocument` (clear, delete)
- `FailedClearInvalidation_IsCompletedByARepeatedClear`
- `Writes_CommitBeforeTheyInvalidate` (update, patch, clear, delete)
- `ReadDuringTheInvalidation_SeesTheWrittenAncestor`
- `ConcurrentChange_IsRetried`
- `SoftDelete_BetweenReadAndWrite_IsNotOverwritten`
- `Patch_OfACacheRow_ContinuesAfterTheArchivedVersions`
- `EmptiedVersion_IsArchived` (write, import)
- `VersionZero_WithContent_IsArchived`
- `UnitOfExecution_WritesAndClearsStoreNoToken`

`Api.Functions/test/MachineRestrictionHttpTests.Handlers.cs` adds `DeleteConfiguration_AnswersWhatTheClearDid`.

Two planned items are not covered by tests:

- The two-step archive of decision 3 has no emulator test. The emulator drops requests of several hundred KB, as the spec's "Out of scope" notes.
- The import theory asserts its version through the result only.

Results with the follow-up fixes, against the Testcontainers emulator:

- `Backend.CosmosDb.UnitTests`: 531 tests. The final run passed 530; the one failure was an emulator `408` on a cache write in `BindingDependencyTests`, and that class passed on the rerun.
- `Api.Functions.UnitTests`: 2255 pass.
- `Backend.Core.UnitTests`: 894 pass.
- `dotnet build src/Platform` reports a single error, which is not related to this change: `Supervisor/Abstraction/test` targets `net6.0`, which `Microsoft.NET.Test.Sdk` 18.3 refuses.

The Linux emulator image (2.14.28) crashed with a core dump several times under the full suite, on a Docker VM with 2 CPUs and 8 GB. Each crash failed the tests that ran afterwards with `408`, `503`, refused connections or an SSL EOF. Restarting the container and running again cleared them. `InsertCachedExecutionsAsync` writes its rows in transactional batches, because a burst of single upserts timed out.

### 1.14 Documentation

- `inheritance.md`, "Cache and descendant freshness": the **Writes**, **Archives** and **Pending invalidation** paragraphs. Step 7 now says "replaced conditionally on the ETag". The code review narrowed the import part of the pending paragraph to the outcomes that finish it.
- `templating.md`, "Cache": the write order, the empty clear that finishes pending clears, and the first insert in a guarded batch.
- `audit-operations.md`, "Configuration writes": the audit before the invalidation, the new sequence diagram, and the costs (archive read, token patch, pending-clears query, retries).
- `export-import.md`: `Unchanged` and `Skipped` finish a pending invalidation, a write conflict fails only its file, and an invalidation failure stops the job.
- `annotation-lifecycle.md`: the `Conflict` error code.
- `definitions.md` already listed the `409` of a lost race and is unchanged.

### Code review

The whole branch was reviewed through four lenses, and each finding was verified independently. Findings upheld for phase 1, all low:

- Assertions such as `x["Prop"]?.Type.Should()…` could not fail when the property was missing. They now assert presence.
- `GetConfigurationVersionContentAsync` let a cache row hide an archived version 0 (see 1.10).
- Unit-of-execution clears still wrote and removed a token (see 1.8).
- `Startup.cs` had lost a space after the conditional `:` (StyleCop SA1003).
- `templating.md` still described the first insert as `CreateItemAsync`, and the listing comment called every version 0 unwritten.

Findings refuted as harmless:

- unused fields of the test seam;
- the unused `Version` of a queried pending clear;
- the remaining generality of `BuildTemplatePredicate` and of the `PatchAsync` overloads.

### Left for later

- The lifecycle snapshots (soft delete, purge, re-create) still upsert `cfv.{V}`, and a restore deletes it. Rows of an earlier lifetime with a colliding version can lose that archive there.
- Decision 8 applies only to the configuration service. `DocumentHistories` leaves emptied versions out of exports, and a cache row still appears as version 0 in the listing.
- The create branch still commits after a concurrent soft delete that loaded no configuration row.
