# Read the Parents of a Soft Delete and a Restore in One ReadMany Call

Linear: [424-229](https://linear.app/424fun/issue/424-229/read-the-parents-of-a-soft-delete-and-a-restore-in-one-readmany-call).

## Overview

This review covers the whole spec [Read the Parents of a Soft Delete and a Restore in One ReadMany Call](../2026-11-15%20Read%20the%20Parents%20of%20a%20Soft%20Delete%20and%20a%20Restore%20in%20One%20ReadMany%20Call.md), in one phase. A soft delete and a restore now read the active parents outside the deleted or restored tree in one read of many items, where they used to make one point read after another. The results are the same: a missing or deleted parent is skipped, only an active one gets its arrays changed, and each changed parent is replaced under the ETag of the read. The change reuses `AnnotationItemStore.ReadNodesAsync`, so it adds no new read code. Commits: `29f379f7f` (the reads and the lifecycle page), `fac0f7397` (instrumentation and tests), `94f7a1b64` (doc corrections from the review). The review of the implementation also found a gap that existed before this change, in the relations wave of both operations. It is described under "Follow-ups" and is not fixed here.

## What Was Done

### 1. The delete reads its outside parents at once

As planned. `CosmosAnnotationService.Deletion.cs`, `DeleteCoreAsync`: before the loop over `removals`, one `store.ReadNodesAsync(removals.Select(group => FullKey.Create(group.First().Parent, fullKey)), withAudit: false)` reads every parent. The loop takes its node from `parents[group.Key]` (the group key is the parent's key text, the key of the dictionary). The order of the groups, the skip of a parent that isn't `Active`, the removals on one entity, and `AddRelationUpdates` are unchanged. With no outside parent, `ReadNodesAsync` makes no request.

### 2. The restore reads its outside parents at once

As planned. `RestoreCoreAsync`: the loop over `outsideParents` became one `ReadNodesAsync(…, withAudit: false)`. Every node it returns goes into `known`, including the `Missing` ones, so `AddToParents` skips them as before. `outsideParents` is unchanged: it still leaves out the written keys and the keys already in `known`. That means the ancestors of the key (from `LoadNodesAsync`) and the restored descendants are not read again.

### 3. What stays

As planned:

- No new helper. `ReadNodesAsync` already returns a node per requested key, `Missing` for a key not found, reads in chunks of 1,000, and takes the item's `_etag`.
- `ReadManyAsync` and `FindDeletedAnnotationsAsync` are unchanged, and so are `LoadNodesAsync`, the fallback point read of `CollectDeletedChildrenAsync`, and `CompleteParentRelationsAsync`.
- The reads stay inside the retried operation.
- A failed read now throws `CosmosRequestFailedException`, a `CosmosException` that `BatchPlan` already throws for failed batches, where the point read threw a plain `CosmosException`. A `404` was never a failure: a parent that isn't found is `Missing` in both cases.

### 4. Test instrumentation

As planned. `StorageInterceptor` records `PointReads` (the id of every point read, from `OnReadAsync`) and `ReadsOfMany` (the ids of every read of many items, one list per request, recorded before `BeforeReadMany` can answer it). Both are concurrent queues that `Reset` clears. The interceptors are created per test, so the records stay small. The class comment of `InterceptedContainer` says what it records.

### 5. Tests

`CosmosAnnotationParentReadTests` (new, a project per test, the service under test over an `InterceptedRepositoryProvider`):

1. `Delete_ReadsItsOutsideParentsInOneReadOfManyItems`: deletes `cnt.shop.blue` with two executions. Checks one read of many items with exactly the subject and the two usages, no point read of them, and that each parent's replace uses the `IfMatchEtag` the item had before the delete. Also checks that the three parents lose `blue`.
2. `Restore_ReadsItsOutsideParentsInOneReadOfManyItems`: restores `usg.shop.billing` with its two executions. Checks one read of many items with exactly `cnt.shop.blue` and `cnt.shop.green`, no point read of them, and that both get `billing` back. The subject is an active ancestor read by `LoadNodesAsync`, so it is not in that read.
3. `Delete_RetriedAfterAConcurrentChange_ReadsItsOutsideParentsAgain` and `Restore_RetriedAfterAConcurrentChange_ReadsItsOutsideParentsAgain`: the first batch of the operation loses to a patch of its item, and the real batch answers `412`. Each test checks that the root's batches were `412` and then a success, that there were two reads of many items with the parents, and the final arrays.

`CosmosAnnotationLifecycleTests.Delete_WithAnOutsideParentDeletedOrExpired_ChangesOnlyTheActiveOne` (a theory, deleted and expired) is the acceptance test. It builds its state with public operations: create `uxe.<s>.<r>.<c>.<u>`, delete `sbt.<s>`, restore `cnt.<s>.<c>` with descendants. After that, the execution and its unit of execution are active, and the usage stays deleted with `<c>` in its `ContextNames`. In the expired case, the usage's item is then removed, as if its `ttl` had run out. Deleting the execution removes `<r>` from the context and leaves the unit active. A deleted usage keeps its `_etag`, its `ContextNames` and its `Deletion`, so it wasn't written. An expired one stays missing.

Checks:

- Built without warnings in the changed files. The first version had one SA1201 (a property after the methods), which was fixed before the commit.
- The four tests of `CosmosAnnotationParentReadTests` fail against the code before the change, because there are no reads of many items. They pass with it.
- With `STORYTELLER_TESTS_USE_CONTAINER=true` (vNext), `CosmosAnnotationParentReadTests`, `CosmosAnnotationLifecycleTests` and `CosmosAnnotationConditionalWriteTests` pass: 44 tests.
- The whole `Backend.CosmosDb/test` suite: see "Test run" below.

### 6. Documentation

`annotation-lifecycle.md`:

- Step 3 of Delete says that the outside parents are read in one read of many items, and replaced under the ETag of that read.
- Step 5 of Restore says the same for the parents outside the restored tree. It says the ancestors of the key come from the reads of step 1, and that a deleted parent gets no names but can still become a root (step 4).

**Deviation:** the spec named only `annotation-lifecycle.md`. The review of the implementation found that the first wording of Restore step 5 claimed too much: it said that every outside parent came from the one read and that a deleted parent is never written. The step was reworded. The review also found that the delete sequence diagram in `audit-operations.md` drew the parent read after the root wave. In fact it runs before the first batch, in this change and before it. The diagram now shows it after the descendant query as one read of many items, and step 5 there says so.

## Review

Three reviewers looked at the diff: behavioural equivalence, tests, and completeness with the docs. A verifier tried to refute each of their findings.

- Equivalence found nothing. The key texts, the `FullKey` of the parents, the partition keys, the `Missing` nodes, the ETag (`_etag` is the value of the point read's header), the order of the relation updates, and the retries all match the old reads.
- The doc wording of Restore step 5 was confirmed and fixed (section 6).
- Two findings were rejected:
  - that the restore tests should also check the ETags of the replaces. The delete test covers the shared path `AddToParents` → `AddRelationUpdates` with the nodes of the read.
  - that the diagram in `audit-operations.md` still drew one read per parent. It drew a single arrow, but in the wrong place, which was fixed anyway.

## Follow-ups

Both operations write the parents' arrays in the relations wave, after the tree's waves have committed. The review confirmed a gap that existed before this change, which the change neither causes nor fixes:

- **Delete:** when a parent changes between the read and the relations wave (`412`), `RetryAsync` runs the delete again. The second attempt finds the root deleted and returns the existing deletion with empty `DeletedKeys`, so the parent keeps the names of the deleted children.
- **Restore:** the same happens to the names a restore adds, and to the root marks of deleted children. A second attempt finds the key active and writes nothing.

A create handles the same case through `CompleteParentRelationsWithRetriesAsync`. A delete and a restore could complete their parents the same way, under fresh ETags, once their tree has committed. This is left for its own issue: [424-281](https://linear.app/424fun/issue/424-281/complete-the-parent-arrays-when-a-delete-or-a-restore-loses-its).

## Test run

The whole `Backend.CosmosDb/test` suite ran in one run with `STORYTELLER_TESTS_USE_CONTAINER=true`, on the vNext emulator: 838 tests, 831 passed, 6 skipped and 1 failed, in 14.5 minutes.

- The 6 skipped tests carry the trait `Emulator=Classic`, so they skip on vNext.
- The failure was `KeyValueEndToEndTests.Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos`. The TLS handshake with its Lowkey Vault container failed (`Received an unexpected EOF`), before any Cosmos DB or annotation code ran. The class passed when it was run again on its own (2 of 2).
