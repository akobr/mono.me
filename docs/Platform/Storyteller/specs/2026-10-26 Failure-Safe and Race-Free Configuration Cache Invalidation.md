# Failure-Safe and Race-Free Configuration Cache Invalidation

Linear: [424-50 Make configuration cache invalidation failure-safe and race-free](https://linear.app/424fun/issue/424-50/make-configuration-cache-invalidation-failure-safe-and-race-free)

Line numbers refer to `origin/main` at `52f4bd9a`. This document changes no application code. The CodeRabbit coding plan attached to the issue was read as a second opinion; where this plan differs, "Decisions" says why. A first draft of this plan was reviewed adversarially before it was committed; the review's findings are folded in.

## Problem

A configuration read caches the effective document (ancestors, templates, own content) in `CalculatedContent` of its item. A write has to clear that cache on every descendant whose effective document includes the written annotation. The review of 424-44 ([2026-10-17 Invalidate Descendant Caches on the First Configuration Write](reviews/2026-10-17%20Invalidate%20Descendant%20Caches%20on%20the%20First%20Configuration%20Write.md)) found three older defects in that invalidation. Each was confirmed again against the current code.

1. **A failed invalidation wedges the configuration.** The update branch archives version *V* in its own call, then invalidates, then writes *V+1*. When the invalidation fails (a failed batch, a 429 after the SDK retries, a 503) or the process dies in between, history *V* exists next to current *V*. From then on:
   - `CreateOrUpdateConfigurationAsync` and `ImportConfigurationAsync` fail with a raw Cosmos `409` on the same history id, which the API answers with `500`; an import fails the whole job instead of the file;
   - `PatchConfigurationAsync` answers `409 Conflict` "changed concurrently, try again" on every attempt;
   - `ClearConfigurationAsync` and `DeleteAsync` ignore their batch response. The batch rolls back, but they still invalidate, write a `ConfigurationDeleted` audit entry and report success;
   - `GetConfigurationVersionsAsync` lists *V* twice.

   Only an annotation lifecycle operation (soft delete and restore, re-create, purge) removes the orphan. The history item otherwise lives for 365 days.
2. **Update and patch invalidate before they write.** A read of a descendant that runs after the descendant was patched, but reads the ancestor before the write, caches the old effective document on the descendant's new ETag. It stays stale until another write clears it.
3. **Invalidation batches run in no order.** The patches go out in transactional batches of at most 100 items per partition, up to 8 at a time. An execution can be recalculated from its usage's stale cache before the usage's batch runs, and cached. The same holds for every template write.

A related gap: a descendant item that a calculation *creates* after the invalidation queried the items is never reached. Templates are rechecked after such a create (`HaveTemplatesChangedAsync`), ancestor configurations are not.

More defects of the same code paths were found while planning. They are fixed here because the new write path touches them:

- The update path writes the new version with an `UpsertItemAsync` without an ETag. A soft delete that patches `AnnotationDeletionId` and `ttl` between the read and the upsert is overwritten: the configuration of a deleted annotation becomes visible again and never expires. The patch path has the same unguarded upsert.
- `PatchConfigurationAsync` on an item without own content (a system cache row, version 0) archives an empty history item `…cfv.{key}.0` and numbers the new version `1`, even when a clear left versions behind. The next archive of version `1` then collides with the history item of the earlier lifetime. The update path did the same before 2026-10-14, so stored data can already hold such a collision.
- A calculation reads the template of each type once and reuses it for the whole calculation. A responsibility (on a unit of execution) and a subject (on an execution and a unit of execution) are calculated twice, through two branches. When a template write commits between the two, the second calculation reads the row after the invalidation patched it, merges the template read before, and caches the old template on the new ETag.
- The update path stores an emptied version (content `{}` with a version above 0) and then archives nothing on the next write, because it decides by the content whether the row is a written version. The version disappears from the listing, and a diff against it fails.

## Current State

`src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`:

- `WriteConfigurationAsync` (lines 375–506), used by `CreateOrUpdateConfigurationAsync` (merge) and `ImportConfigurationAsync` (replace):
  - reads the item without its ETag (384);
  - create branch (391–425): `CreateItemAsync` (418) without a retry, so a cache row that a read created meanwhile surfaces as a raw `CosmosException` 409; then invalidates (422) and audits (423);
  - update branch: no-op returns (437–441, 448–454); history `CreateItemAsync` on its own (481), only when the row has content; version allocation for a row without content (486–488); invalidation (491); `UpsertItemAsync` of the stale read with the new content, without an ETag (502); audit (503).
- `PatchConfigurationAsync` (508–599): no-op return (531–534); invalidation *before* the write (542); batch `CreateItem(history V)` + `UpsertItem(V+1)` (570–573); a 409 becomes `ConflictException(ErrorCodes.Conflict)` (577–580). It archives and numbers as if the item always were a written version (545–568).
- `ClearConfigurationAsync` (601–646): returns early when the item is missing or has no content (617–622); batch `CreateItem(history V)` + `DeleteItem` (640–643) with the response not checked; invalidates (644) and audits (645).
- `DeleteAsync` (648–664) → `ForceDeleteConfigurationAsync` (1163–1209): a row without content is deleted with `DeleteItemStreamAsync`, response not checked and no history (1179–1187); a row with content goes through an unchecked batch `CreateItem(history V)` + `DeleteItem` (1189–1208). `DeleteAsync` then invalidates unconditionally (658) and audits (660–663). It has no production caller; tests use it.
- `GetConfigurationVersionsAsync` (263–305) appends the current row to the history rows unconditionally (299–302). `GetConfigurationVersionContentAsync` (307–343) prefers the history item over the current row.
- `ImportConfigurationAsync` (719–850) returns `Unchanged` for an equal document (781–784) and `Skipped` for a different one under `Skip` (786–792), both before any write. A retries-exhausted write has to surface as `ConflictException(ErrorCodes.Conflict)` so that `ViewTransferService.IsWriteConflict` (`Backend.Core/src/Transferring/ViewTransferService.cs`, around 478–493) fails only the file.
- `CalculateAndCacheConfigurationAsync` (941–1053): reads the item with its ETag (951); returns a cached `CalculatedContent` directly (958–961); walks the ancestors recursively (966–973) with no visited set; merges the type template from a per-calculation dictionary (`TryAutogeneratePropertiesAsync`, 864–886); caches on an existing item with `IfMatchEtag` (995–1004); creates a missing item with the cache (1012–1036) and then clears it when `HaveTemplatesChangedAsync` (896–908) finds a changed template ETag (1039–1049). Between the create and that check, the cache is visible to other calculations.
- `InvalidateConfigurationsAsync` (1055–1161): one predicate per written type. The Responsibility predicate matches every configuration item of the partition, the written item included (1063–1069). Subject runs two calls, contexts in the subject partition and then usages, executions and units of execution across partitions (1104–1120).
- `EnsureAnnotationNotDeletedAsync` (1259–1271), `AuditConfigurationAsync` (1275–1291, best effort).

`src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/ConfigurationCacheInvalidator.cs`:

- `InvalidateAsync` (66–87 cross-partition, 89–112 one partition) enumerates the matching ids, then `PatchAsync` (121–137) splits them into chunks of 100 (`MaxBatchOperations`) and runs up to 8 (`MaxParallelBatches`) at once, with no order between chunks or partitions. A chunk that fails with a 404 inside falls back to one-by-one patches (157–174).
- `InvalidateForTemplateAsync` (60–64) runs one cross-partition query over all types of `TemplateAffectedTypeCodes` (24–40).

`src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationTemplateService.cs` is the precedent: a template write is one transactional batch with ETags (history `CreateItem`, guarded `ReplaceItem`/`DeleteItem`, a state row with `IsInvalidationPending = true`), retried up to `MaxRetries = 3` through `TryExecuteWriteAsync` (573–591) on 409/412/404. It invalidates after the commit and clears the flag with a patch guarded by the state ETag (607–624). Every no-op request finishes a pending invalidation (`CompletePendingInvalidationAsync`, 593–605).

Other writers of configuration rows (`{view}.cnf.{key}`), none of which invalidates and none of which uses a configuration ETag: the annotation soft delete (patch of `AnnotationDeletionId` and `ttl`, `Annotating/CosmosAnnotationService.Deletion.cs` 233–249), restore (patch, 422–482), purge (556–566), and re-create (`Annotating/CosmosAnnotationService.cs` 417–427). They write history snapshots of rows with content with `UpsertItem`, copying fields explicitly; re-create and purge delete a row without content without a snapshot.

`src/Platform/Storyteller/Api.Functions/src/V1/ConfigurationHttp.cs`, `DeleteConfiguration` (394–437): answers `404` (or `409 AnnotationDeleted`) when `HasConfigurationContentAsync` is false (426–433), before `ClearConfigurationAsync` runs (435). A repeated `DELETE` after a committed clear therefore never reaches the service. The `409` responses of `SetConfiguration` (225), `PatchConfiguration` (314) and `DeleteConfiguration` (404) already exist; their descriptions name only the schema and the deleted annotation.

Serialization: typed writes go through the client serializer (`IgnoreNullValues = true`, default values included), stream reads through `DeserializeNewtonsoft` with `MissingMemberHandling.Ignore`. `Microsoft.Azure.Cosmos` is 3.57.1. Checked against the emulator while planning: `PatchItemRequestOptions.FilterPredicate` answers `412` on a mismatch and `404` for a missing item; in a batch, a failed `ReplaceItem`/`DeleteItem` with `IfMatchEtag` reports `412` (or `404`) as the batch status with `424` on the other operations, and a `CreateItem` of an existing id `409`; `CreateItemAsync` and `ReplaceItemAsync` return the new ETag with `EnableContentResponseOnWrite = false`. Cosmos rejects a transactional batch above 2 MB; `Annotating/BatchPlan.cs` keeps its batches below 1.5 MB.

Tests (`src/Platform/Storyteller/Backend.CosmosDb/test`) run against a real emulator. `Startup.UseContainer` is a constant `false`, so the fixture always targets the local emulator at `localhost:8081` and deletes every database there. There is no seam to fail or observe a single Cosmos operation; `ReplaceInterceptor` sees only single-document `PUT`s, and transactional batches are opaque to it. All Cosmos test classes share one collection and run one after another.

Docs that state the defects: `docs/Platform/Storyteller/inheritance.md` "Cache and descendant freshness" (187–218, gaps at 203 and 209–214; line 85 says "upserted" for what is a guarded replace), `templating.md` "Cache" (216–260), `audit-operations.md` (the write description at 304, the sequence diagram at 306–319, the costs at 321–325), `export-import.md` (equal document is `Unchanged`, 384).

## Proposed Changes

### Decisions

1. **A configuration write never overwrites history.** A write that archives version *V* first reads `…cfv.{key}.{V}`:
   - missing: the archive is created (`CreateItem`) together with the write;
   - present with the same `Content` and `Modified` as the row: *V* counts as archived. This is the orphan the old code left behind, so it stops blocking;
   - present with different content: a collision with an earlier lifetime (see Problem). The current content goes after the highest archived version *H*: when `…cfv.{key}.{H}` already equals the row (an earlier attempt archived it there), it counts as archived at *H*; otherwise it is created at *H+1*. The write continues after it. Nothing is overwritten, and a retry does not archive twice.

   An `UpsertItem` of the archive, as first drafted, would make the orphan harmless too, but would silently replace the history of the colliding case. `CreateItem` alone (the CodeRabbit plan, the template service) keeps failing on every orphan. The lifecycle snapshots (soft delete, purge, re-create) still upsert `…cfv.{key}.{V}`; see "Out of scope".
2. **Every write is guarded by the ETag of its read, and retried.** The archive operation and `ReplaceItem(V+1)` or `DeleteItem` with `IfMatchEtag` are one transactional batch. The ETag also catches a soft delete, a restore and a re-create, which change the row without changing `Version`. A reader caching a calculation or an invalidation patch also changes the ETag, which costs a retry, not a wrong result. A write reads again and retries up to `MaxRetries = 3` times (four attempts), as the template and schema services do, and then throws `ConflictException(..., ErrorCodes.Conflict)` (`409`), which the view import already reports per file.
3. **An archive and a write that do not fit one batch go one after the other.** When the estimated payload (the archived and the new content, plus an allowance per operation) is above the 1.5 MB of `BatchPlan`, the archive is created first and the guarded write follows on its own. Decision 1 makes this safe: if the write then fails, the archive equals the row and counts as archived on the next attempt. Today the update path writes the two separately, so a single batch would halve the largest configuration a `PUT` can update.
4. **Write first, audit, then invalidate.** Every write commits, then writes its audit entry, then invalidates. A committed change is audited even when its invalidation fails. With the invalidation after the write, a descendant calculation that read the descendant after its patch also reads the ancestor after the write.
5. **The pending marker of a write is a token on the configuration row.** The batch that writes a version sets `ConfigurationEntity.PendingInvalidationId` to a new GUID, so the marker commits atomically with the version. After the invalidation it is removed with a patch whose `FilterPredicate` is `FROM c WHERE c.PendingInvalidationId = '{token}' AND NOT IS_DEFINED(c.AnnotationDeletionId)`:
   - a newer write sets a new token, so an older clear never removes a marker it does not own. A version number would not do: versions can repeat after a re-create or a purge deleted a row without archiving it;
   - an ETag would fail whenever a reader cached the row or another invalidation patched it in between;
   - a soft-deleted row is not rewritten, so its expiry is not pushed back; it keeps the marker until a restore, and the next request finishes it.

   `412` and `404` from the patch are expected and leave the row as it is. A separate state row, as the templates have, is not used: broad annotation queries and view discovery would see it, and the lifecycle batches would have to handle it.
6. **The pending marker of a clear is a token on the history item it writes.** A clear deletes the row, so the row cannot hold the marker. The clear's batch writes history *V* with `ConfigurationHistoryEntity.PendingInvalidationId` set (or sets it on an existing equal archive with a `PatchItem`); it is removed after the invalidation, filtered on the token. `ClearConfigurationAsync` returns whether it cleared content, so the `DELETE` route can call it first and still answer `404` when there was nothing to clear. Rejected: invalidating on every clear that finds nothing (each `404` would drop all descendant caches), and a tombstone row (it shows as a version, with `HasContent = false` in the listing and as `Empty` in the composition).
7. **Every write request first finishes what an earlier request left pending on the same configuration.** Right after its read and the deleted-annotation check, and before it evaluates its input, a write, patch, clear or delete finishes the row's pending invalidation when the row has a token, and the pending clears of the key when the row is missing or has no content. Recovery then does not depend on the request being repeatable: a repeated `PATCH` whose `test` operation now fails, a write rejected by a schema changed meanwhile, or a `PATCH` of a cleared key still finish it. An import finishes the row's marker when its outcome writes nothing (`Unchanged`, `Skipped` under `Skip`); its writes go through the write path. The cost is one query in the key's partition when the row is missing or has no content. Finishing removes the token, which changes the row's ETag, so the attempt reads the row again before it evaluates the input; otherwise every such attempt would lose its guarded write. A unit of execution has no descendants, so its writes set no token at all.
8. **A cache row is a row with version 0 and no content; every other row is a written version.** A calculation creates cache rows; every write gives version 1 or more, and rows with version 0 and content come from the time before versions were counted (before November 2024), as the composition already assumes (`ToConfigurationSource`). A write archives every written version, also an empty one or one at version 0, and a cache row is never archived; the next version after a cache row or a version 0 continues after the highest archived one. This replaces the content test of today, which loses emptied versions. The no-op check follows: a document equal to a written version's content writes nothing, also `{}` over an emptied version; over a cache row any document is the first one.
9. **Invalidate level by level, and enumerate each level only after the previous one is patched.** The levels follow `BuildInheritanceGraph`: 0 = Responsibility, Subject; 1 = Unit, Usage, Context; 2 = Execution; 3 = Unit of execution. A calculation that runs after its own item was patched then reads only ancestors that are already patched or written. Enumerating late also covers items created meanwhile: an item a calculation creates after its level was queried is created after every shallower level was patched.
10. **A cache is written only under an ETag read before everything the calculation used.** A calculation reads its row first, then calculates the ancestors, then reads the template of its type, then merges; the template is read on every calculation, not once per request. A missing row is first created without a cache, and the calculation then runs again under the created ETag, so the cache is never visible before it is guarded. This replaces the template recheck after a create (`HaveTemplatesChangedAsync`) and makes an ancestor recheck unnecessary. The first draft rechecked ancestors after creating a cached row; the review showed that another calculation can merge such a cache before the recheck clears it.
11. **No read-side replay and no sweeper.** A pending invalidation is finished by the next write request on the same configuration, or by any later write that invalidates the same descendants. Reads keep serving a cache until then. This is the template contract, documented as a limit.
12. **Differences from the CodeRabbit plan.** It was written before 424-44 merged; `main` already invalidates after a create. It archives with `CreateItem` (see 1), clears its flag filtered on the version (see 5), replays a clear whenever a clear finds nothing, which the `DELETE` route never reaches (see 6), replays only on no-op exits (see 7), and rechecks ancestors after a create (see 10). It does not cover the unguarded soft-delete overwrite, the patch version allocation, the template reuse, the emptied versions or the batch size. Its four phases become two, because the guarded write (its phase 1) cannot come before the invalidation moves behind the write (its phase 2): the Responsibility invalidation patches its own row and would fail every guarded write.

### Phase 1: Atomic, write-first configuration writes with recoverable invalidation

#### 1.1 Entities

- `Entities/Configurations/ConfigurationEntity.cs`: `public string? PendingInvalidationId { get; init; }` with a comment: the token of the write whose cache invalidation has not finished.
- `Entities/Configurations/ConfigurationHistoryEntity.cs`: the same property, with a comment: the token of the clear or delete that archived this version, while its invalidation has not finished.
- Null is not written (`IgnoreNullValues`), so rows without a pending invalidation carry nothing extra. The mappings to API models and the lifecycle snapshots copy fields explicitly, so the tokens never leave the store and snapshots carry none.

#### 1.2 Batch execution and retries

In `CosmosConfigurationService`:

- `private const int MaxRetries = 3;` and `private const long MaxBatchPayloadBytes = 1_500_000;`
- `private async Task<bool> TryExecuteWriteAsync(TransactionalBatch batch, string annotationKey, string partitionKeyValue)`: executes and disposes the response. `409`, `412` and `404` return `false` (another writer archived the version, changed or deleted the row, or created it). Another failure is logged with the activity id and the error message, as the patch path does today, and throws `ConfigurationStorageException` with the status. The top-level `StatusCode` is the failing operation's; the others report `424`.
- `private static ConflictException ConcurrentChange(string annotationKey)` returns `new ConflictException($"The configuration '{annotationKey}' was changed concurrently, try again.", ErrorCodes.Conflict)`, thrown when the retries run out.
- `ToHistory(ConfigurationEntity current, ulong version, string? pendingInvalidationId)` replaces the four copies of the history construction (lines 468, 548, 627, 1194).
- Every attempt reads the row again with `TryReadItemWithETagAsync` and runs `EnsureAnnotationNotDeletedAsync` again, so a soft delete between attempts answers `409 AnnotationDeleted`. Each attempt then finishes pending work (1.8) before it looks at the input, and when it finished something, reads the row and checks the annotation once more (`ReadForWriteAsync`).

#### 1.3 The archive

`private async Task<ArchivePlan> PlanArchiveAsync(FullKey key, IContainerRepository repository, ConfigurationEntity existing)`, for a written version (decision 8):

- reads `{view}.cfv.{key}.{existing.Version}`;
- missing: `Create`, at `existing.Version`;
- equal `Content` and `Modified`: `Archived`, at `existing.Version` (no history operation);
- different: reads the archive at the highest archived version *H*; `Archived` at *H* when it equals the row, otherwise `Create` at *H+1*.

The next version is the archived version + 1, or `GetHighestArchivedVersionAsync + 1` after a version 0. A cache row has no archive; its next version is `GetHighestArchivedVersionAsync + 1` (the rule of lines 486–488, now keyed on decision 8 and also used by the patch).

#### 1.4 Writing the next version

`private async Task<ConfigurationEntity?> TryWriteNextVersionAsync(FullKey key, IContainerRepository repository, ConfigurationEntity existing, string etag, JObject newContent, AuditActor actor)`, shared by update and patch:

- `next = existing with { Version = …, Content = newContent, Modified = AuditStamp.Of(actor, now), CalculatedContent = null, CalculatedContentHash = null, PendingInvalidationId = Guid.NewGuid().ToString("N") }`, with no token for a unit of execution;
- the archive's `CreateItem(ToHistory(existing, archivedVersion, null))` when the plan says `Create`, and `ReplaceItem(existing.Id, next, IfMatchEtag = etag)`: one batch, or the archive first and the guarded replace on its own when the payload exceeds `MaxBatchPayloadBytes` (decision 3);
- returns `next`, or `null` when an execution asks for a retry.

#### 1.5 Create and update (`WriteConfigurationAsync`)

The method becomes a loop of at most `MaxRetries + 1` attempts. Each attempt works on `value.DeepClone()`, because `RemoveRequested` and `ApplyPatchRequested` change their input.

- **Missing row:** apply the directives, validate (unless `force`), version = highest archived + 1, a new token, write with a batch `CreateItem` through `TryExecuteWriteAsync`. A `409` (a read created a cache row meanwhile) retries into the update branch. Then audit and complete the invalidation (1.8).
- **Existing row:** merge or replace and apply the directives as today. A document equal to the content of a written version is a no-op and returns (decision 8). Otherwise validate, `TryWriteNextVersionAsync`, audit, complete the invalidation.
- A `404` from the guarded replace (the row was deleted) retries into the create branch.

#### 1.6 Patch (`PatchConfigurationAsync`)

The same loop. Each attempt applies the JSON Patch to the fresh content (`ApplyPatch` does not change its inputs), keeps the `412 PatchTestFailed` of a failed `test` operation, returns on a no-op, validates, writes with `TryWriteNextVersionAsync`, audits and completes the invalidation. A missing row throws `ConfigurationNotFoundException` after the pending clears are finished. The comment that the upsert cannot be guarded (lines 575–576) goes away with the reason it gave.

#### 1.7 Import (`ImportConfigurationAsync`)

In a real run (not a dry run), an outcome that writes nothing (`Unchanged`, or `Skipped` with `DocumentExists`) finishes the existing row's pending invalidation, as the template import does. A write goes through `WriteConfigurationAsync`. A failed invalidation still fails the whole job, as for templates; the marker keeps it recoverable, and importing again finishes it.

#### 1.8 Finishing pending work

- `CompleteInvalidationAsync(FullKey key, IContainerRepository repository, string token)`: `InvalidateConfigurationsAsync`, then `PatchItemStreamAsync(id, [Remove /PendingInvalidationId], FilterPredicate = "FROM c WHERE c.PendingInvalidationId = '{token}' AND NOT IS_DEFINED(c.AnnotationDeletionId)")`. `412` and `404` are expected; another status is logged as a warning and not thrown, because the write and the invalidation are done. An exception from the invalidation propagates and leaves the marker: the request answers `500` with the change committed, as for templates.
- `CompletePendingWorkAsync(FullKey key, IContainerRepository repository, ConfigurationEntity? existing)`, run by every attempt right after the read (decision 7):
  - a row with a token: `CompleteInvalidationAsync` with that token;
  - a missing row or one without content: `CompletePendingClearsAsync`.
- `CompletePendingClearsAsync(FullKey key, IContainerRepository repository, bool always = false)`: queries `id` and `PendingInvalidationId` of the key's history items that carry a token (one query in the key's partition, id prefix `{view}.cfv.{key}.`). When none is found and `always` is false, it returns. Otherwise it invalidates and removes exactly those tokens (filtered patch, `412`/`404` expected). The tokens are collected before the invalidation, so a clear that commits during it keeps its own.
- A changed write runs its own invalidation after the commit; whatever was pending before has already been finished by the attempt.

#### 1.9 Clear and delete

- `IConfigurationService.ClearConfigurationAsync` returns `Task<bool>`: `true` when stored content was cleared. Its doc comment says that every clear first finishes the invalidation of an earlier clear left pending.
- `ClearConfigurationAsync`, per attempt: read with the ETag, `EnsureAnnotationNotDeletedAsync`, finish pending work (1.8). A missing row or one without content returns `false`, without an audit entry. Otherwise, with a new token: the archive (`CreateItem` of history *V* with the token; `PatchItem` setting the token on an equal archive; `CreateItem` at the next free version on a collision) and `DeleteItem(id, IfMatchEtag = etag)` in one batch through `TryExecuteWriteAsync`. After the commit: audit `ConfigurationDeleted` with the archived version, `CompletePendingClearsAsync` (which finds the new token), return `true`. The clear's archive and delete always fit one batch.
- `DeleteAsync` → `ForceDeleteConfigurationAsync`, which retries the same way and returns the archived version or `null`:
  - a written version (decision 8, also an empty one): the same guarded batch as the clear;
  - a cache row: `DeleteItemStreamAsync` with `IfMatchEtag`; `404` is success, `412` retries, anything else throws `ConfigurationStorageException`.
  - `DeleteAsync` audits when a version with content was archived, then calls `CompletePendingClearsAsync(always: true)`: it invalidates unconditionally, as today, and removes the tokens it found.
- `ConfigurationHttp.DeleteConfiguration` calls `ClearConfigurationAsync` directly: `true` answers `200`, `false` answers `404`; a `StorytellerException` (`409 AnnotationDeleted`, `409 Conflict`) is mapped through `ErrorResponseMapping.ToActionResult`, as in the other write handlers. `HasConfigurationContentAsync` is no longer called there.
- The test doubles follow: `Backend.Core/test/Transferring/TransferFakes.cs` gets the new signature, and `Api.Functions/test/MachineRestrictionHttpTests.Handlers.cs` returns `true` from `ClearConfigurationAsync` instead of from `HasConfigurationContentAsync`.

#### 1.10 Version listing and content

- `GetConfigurationVersionsAsync` skips empty history rows with version 0 (archives of cache rows, written by the old patch path) and, when the current row has a version above 0, history rows whose version is not lower than the current one, as `DocumentHistories.Build` does for exports. An orphan is hidden; it is never rewritten, and a later write counts it as archived.
- `GetConfigurationVersionContentAsync` returns the current row when its version is the requested one, and the history item otherwise.

#### 1.11 HTTP descriptions

The `409` descriptions of `SetConfiguration`, `PatchConfiguration` and `DeleteConfiguration` add "or the configuration was changed concurrently too many times (ErrorCode Conflict), and the request can be repeated". The status codes and body types stay, so the OpenAPI shape and the SDKs do not change; the SDKs are not regenerated.

#### 1.12 Test seam

In `src/Platform/Storyteller/Backend.CosmosDb/test`:

- `Startup` reads `STORYTELLER_TESTS_USE_CONTAINER` like `Access.Certificates/testint/CosmosFixture.cs`, and rewrites the Testcontainers connection string to `https`, which the current emulator image serves. The suite can then run without touching the local emulator.
- `InterceptedContainer : Container` forwards all 46 abstract members to the real container. It wraps `CreateTransactionalBatch` in an `InterceptedBatch : TransactionalBatch`, which records each operation (kind, id, `IfMatchEtag`). A shared `StorageInterceptor` numbers the start and the end of every batch execution in one sequence and calls test hooks:
  - before a batch executes: return a status to fail it with a `FailedBatchResponse` (a subclass over the protected constructor, `424` on all but the first operation), or run an action, for example a concurrent write;
  - after a batch executed;
  - after a point read, with the id.
- `InterceptedRepositoryProvider : IContainerRepositoryProvider` wraps the organization containers of the shared provider. A test builds its own `CosmosConfigurationService` (and `CosmosConfigurationTemplateService`) over it with `ActivatorUtilities.CreateInstance`, so the shared singletons stay untouched and the setup through the annotation service is not recorded. LINQ queries run on the inner container and are not recorded.

No new package. Not a mock: every operation still runs against the emulator.

#### 1.13 Tests (phase 1)

A new `CosmosConfigurationRecoveryTests` class, one project per test, small trees:

- `FailedInvalidation_CommitsTheWrite_AndARepeatedRequestCompletesIt` — a `[Theory]` over update, patch, create (first write of an ancestor) and import. A cached descendant exists; every invalidation batch fails with `503`. The call throws `ConfigurationStorageException`; the new version is stored with a token; the descendant is still cached. Without faults, the same request succeeds without a new version, the descendant is no longer cached and the token is gone. A different write then succeeds with the next version.
- `FailedInvalidation_IsCompletedByARequestThatFails` — a patch with a `test` operation commits and its invalidation fails. Repeating it answers `412 PatchTestFailed`, and still clears the descendant's cache and the token.
- `FailedInvalidation_IsCompletedByADifferentWrite_InOneAttempt` — after a failed invalidation, a different write finishes it and stores its version with exactly one write batch.
- `OrphanHistory_OfTheCurrentVersion_CountsAsArchived` — a `[Theory]` over write, patch, import, clear and delete. A history item equal to the current version is inserted raw. The operation succeeds, the version listing holds each version once before and after, and the history item is unchanged.
- `CollidingHistory_IsNeverOverwritten` — a history item of the current version with other content is inserted raw. A write archives the current content as the next free version and stores the one after; the colliding item keeps its content.
- `CollidingHistory_ArchivedByAnEarlierAttempt_IsNotArchivedAgain` — next to the colliding item, the archive an earlier attempt wrote at the next free version is inserted raw. The write counts it as archived and creates no second copy.
- `FailedClearOrDelete_Throws_AndKeepsTheDocument` — the clear's (delete's) batch fails with `503`. The call throws, the document and its version stay, and the annotation history has no `ConfigurationDeleted` entry.
- `FailedClearInvalidation_IsCompletedByARepeatedClear` — the clear commits and its invalidation fails: the call throws, the content is gone, history *V* carries a token, the descendant is still cached. A second clear returns `false`, clears the descendant's cache and removes the token. A third clear finds nothing pending and writes no batch.
- `Writes_CommitBeforeTheyInvalidate` — update, patch, clear, delete: the write batch ends before the first invalidation batch starts.
- `ReadDuringTheInvalidation_SeesTheWrittenAncestor` — a hook after the invalidation batch that patched the descendant reads it through the shared service. That read and the read after the write contain the new value. (With the old order, the hook's read cached the old document.)
- `ConcurrentChange_IsRetried` — a hook before the first write batch changes the row with a raw patch; the write retries and stores the next version once. A hook on every attempt makes it throw `ConflictException` with `Conflict`.
- `SoftDelete_BetweenReadAndWrite_IsNotOverwritten` — a hook before the write batch soft-deletes the annotation; the write throws `409 AnnotationDeleted` and the row keeps `AnnotationDeletionId`.
- `Patch_OfACacheRow_ContinuesAfterTheArchivedVersions` — after a clear left versions 1–2 behind and a read created a cache row, a patch stores version 3 and archives nothing.
- `EmptiedVersion_IsArchived` — a `[Theory]` over a write and an import. A patch that removes every property stores an empty version 2; a `PUT` of `{}` then changes nothing; the write or import of a document archives version 2 and stores version 3. The listing has versions 1–3, and the diff of version 3 works. (The old update path lists 1 and 3, and the diff fails.)
- `VersionZero_WithContent_IsArchived` — a row with content at version 0 (set raw) is archived as version 0 by the next write, which stores version 1.
- `Api.Functions/test`: `DeleteConfiguration` answers `404` when `ClearConfigurationAsync` returns `false`, and `200` when it returns `true`.

The existing suite has to pass unchanged, apart from the test doubles in 1.9.

#### 1.14 Documentation (phase 1)

In `docs/Platform/Storyteller/`:

- `inheritance.md`, "Cache and descendant freshness": the write order (commit, audit, invalidate), the guarded batch and its retries, the row and history markers with their completion by the next request; the paragraph on the invalidate-before-write order removed. Line 85 says "replaced conditionally on the ETag" instead of "upserted".
- `templating.md` "Cache": the order of the clearing writes; the empty clear finishing pending clears.
- `audit-operations.md`: the write description (304), the sequence diagram (306–319: read with ETag, finish pending work, guarded batch, audit, invalidate, remove the marker) and the costs (321–325: the extra history read of a write, the query of a clear that finds nothing, retries).
- `export-import.md`: an equal document is `Unchanged` and finishes a pending invalidation; a skipped one too.
- `annotation-lifecycle.md`: the `Conflict` error code also covers a configuration changed concurrently three times in a row.
- `definitions.md` and the configuration write status codes already list `409` with `ErrorResponse` for a lost race; checked, unchanged unless they contradict.

### Phase 2: Level-ordered invalidation and guarded cache writes

#### 2.1 `ConfigurationCacheInvalidator`

- `internal sealed record InvalidationTarget(string TypeCode, Expression<Func<ConfigurationEntity, bool>> Predicate, PartitionKey? PartitionKey)`: one type code, a predicate that selects items of that type only, and the partition, or `null` for a cross-partition query.
- `public static async Task InvalidateAsync(IContainerRepository repository, IReadOnlyCollection<InvalidationTarget> targets)` replaces both overloads. It groups the targets by level (0: `rst`, `sbt`; 1: `unt`, `usg`, `cnt`; 2: `exe`; 3: `uxe`). Per level, in order: query the ids of every target of the level (each query still excludes items with `AnnotationDeletionId`), group them by partition, patch them with the existing chunks, parallelism, patch content, 404 fallback and exceptions, and await all of it. Only then is the next level queried. A failure stops the levels and propagates; the marker covers the rest.
- `InvalidateForTemplateAsync` builds one cross-partition target per type of `TemplateAffectedTypeCodes`, each with the template predicate restricted to that type, and goes through the same entry point. `CosmosConfigurationTemplateService` does not change.

#### 2.2 Targets of `InvalidateConfigurationsAsync`

Every predicate keeps `ProjectName`, `ViewName` and the `{view}.cnf.` id prefix of today. The written item is no longer a target: its own write nulls its cache.

| Written | Level 1 | Level 2 | Level 3 | Partition |
| --- | --- | --- | --- | --- |
| Responsibility | `unt.*`, `usg.*` | `exe.*` | `uxe.*` | the responsibility partition |
| Unit | – | – | `uxe.*` ending `.{unit}` | the responsibility partition |
| Subject | `cnt.*` (subject partition), `usg.{S}.*` (cross) | `exe.{S}.*` (cross) | `uxe.{S}.*` (cross) | as noted |
| Usage | – | `exe.{S}.*` | `uxe.{S}.*` | the responsibility partition |
| Context | – | `exe.{S}.*` ending `.{C}` (cross) | `uxe.{S}.*` containing `.{C}.` (cross) | cross-partition |
| Execution | – | – | `uxe.{S}.{R}.{C}.*` | the responsibility partition |
| Unit of execution | – | – | – | – |

The Subject write turns from two calls into one call with four targets, so contexts and usages share level 1.

#### 2.3 Guarded cache writes

`CalculateAndCacheConfigurationAsync(InheritanceGraphNode node, IContainerRepository repository)` loses the template dictionary:

1. Read the row with its ETag. A cached `CalculatedContent` is returned.
2. Calculate each direct ancestor (recursively) and merge it.
3. Read the template of the node's type and merge it (`TryAutogeneratePropertiesAsync` reads it on every call).
4. Merge the row's own `Content`.
5. An existing row with a non-empty result: replace with `IfMatchEtag` from step 1, as today.
6. A missing row with a non-empty result: create the row without a cache (`Content = {}`, version 0, `Modified` by `System`). A `409` keeps the other one and returns the result. Otherwise calculate again from step 2 under the created ETag, and replace with it.

`HaveTemplatesChangedAsync` and `TemplateSnapshot` are removed. A missing row with an empty result still creates nothing, so reading an annotation without any document leaves no row, as today. The calculation of a node that was missing does the walk twice, the second time mostly over the ancestor caches the first walk wrote.

#### 2.4 Why no stale cache survives a completed write

To be written into `inheritance.md`. Every cache is written with an ETag that the row had before any read its calculation used (step 1 or the create in step 6), so:

- A descendant read before its patch cannot cache: the patch changed its ETag.
- A descendant read after its patch reads its ancestors and its template after them. The ancestors were written or patched at an earlier level, and every cache they hold was calculated from reads after their own patch (by the same rule, level by level). The template was written before the invalidation started.
- A row created after its level was queried was created after every shallower level was patched, and its cache is calculated from reads after the create.
- A row created before its level was queried is reached by that query and patched; its cache write either happened before the patch, which clears it, or fails on the changed ETag.

Limits that stay: reads serve a stale cache while an invalidation runs or stays pending; a pending invalidation is finished only by a write request; writes made directly in Cosmos bypass all of it; old binaries during a deployment overlap rewrite whole rows and drop a marker; the argument assumes the invalidation queries see every item created before they run, which Session consistency does not guarantee across clients.

#### 2.5 Tests (phase 2)

- `ResponsibilityWrite_InvalidatesLevelByLevel` — a responsibility partition with more than 100 cached rows on one level (inserted raw: the invalidation needs only the id, the project and the view), and a small real tree. Writing the responsibility: every batch of a level ends before any batch of the next level starts, the responsibility's own row is in no batch, the raw rows are no longer cached, and the units of execution of the real tree read the new value.
- `ResponsibilityTemplateWrite_InvalidatesLevelByLevel` — the same order through `CosmosConfigurationTemplateService` over the intercepted provider, starting with the responsibility's own row.
- `AncestorWritten_DuringTheCreateOfARow_IsNotCachedOld` — a hook after the calculation of a unit first reads the responsibility writes the responsibility through the shared service. The unit row the calculation creates ends up with the new value or without a cache, and a unit of execution read afterwards has the new value. (With the old code, the created row kept the old value.)
- `TemplateWritten_BetweenTwoVisitsOfARow_IsNotCachedOld` — a hook after a unit-of-execution calculation first reads the responsibility template writes that template through the shared service. The responsibility row is not cached with the old template afterwards, and the unit of execution reads the new template value.
- The existing template and invalidation tests (`Invalidation_MoreThan100ConfigurationsInPartition`, `SubjectTemplateWrite_InvalidatesDescendantsAcrossPartitions`, the first-write theory, the composition tests that count rows) pass unchanged.

#### 2.6 Documentation (phase 2)

- `inheritance.md`: the descendant table gets a level column; the level order, the guarded cache writes and the argument of 2.4 with its limits; gap 3 and the created-item gap removed.
- `templating.md` "Cache": a missing row is created first and calculated under its ETag; the template is read on every calculation; the template invalidation runs level by level.

### Out of scope

- The lifecycle snapshots: soft delete, purge and re-create upsert `…cfv.{key}.{V}` of the row's version, and a restore deletes it when it exists. On a row whose version collides with an archive of an earlier lifetime (data the old patch path can have left), they still replace or delete that archive. Fixing that belongs to the lifecycle code.
- Guarding the annotation lifecycle writes (soft delete, restore, purge, re-create) with configuration ETags. On an existing row, the guarded configuration write now loses to them instead of overwriting them. The create branch (no row yet) still commits after a concurrent soft delete that loaded no configuration row; closing that needs a guard on the annotation item in the same batch, which would change the annotation's ETag on every first write. It stays a documented gap.
- Decision 8 outside the configuration service: the lifecycle snapshots and `DocumentHistories` (exports) still decide by content, so a re-create or a purge drops an emptied version without a snapshot, and an export leaves emptied versions out. A version 0 cache row still appears in the version listing as today.
- An emulator test of the two-step archive (decision 3): the emulator drops requests of several hundred KB, so the path is covered by reasoning only.
- Merging the two history queries of a first write (the highest version and the pending clears) into one; a listing of all versions would cost more than the `MAX` it replaces for keys with many versions.
- A sweeper or a read-side replay of pending invalidations.
- Regenerating the SDKs for the changed `409` descriptions.
