# Read the Parents of a Soft Delete and a Restore in One ReadMany Call

Linear: [424-229](https://linear.app/424fun/issue/424-229/read-the-parents-of-a-soft-delete-and-a-restore-in-one-readmany-call).

## Problem

A soft delete and a restore update the relation arrays of the active parents outside of the deleted or restored tree: a deleted execution leaves `Usage.ContextNames` and `Context.ResponsibilityNames`, a restored one gets back into them. Before the write, each operation reads every such parent, and it reads them one after another: a point read, its answer, the next point read. An operation with N outside parents waits for N round trips in a row before its first batch. One read of many items (Cosmos DB `ReadManyItems`) answers all of them in one round trip.

The gain is time, not request units: a point read costs about 1 RU, and a read of many items about as much per item.

## Current State

All in `src/Platform/Storyteller/Backend.CosmosDb/src/Annotating/`.

- `CosmosAnnotationService.Deletion.cs`, `DeleteCoreAsync` (lines 279–311): `removals` groups the parent relations of the deleted annotations by the parent's key text, without the parents that are deleted too. The loop over the groups calls `store.LoadAsync(FullKey.Create(group.First().Parent, fullKey), withConfiguration: false, withAudit: false)` once per group and waits for it, skips a parent whose `State` is not `Active`, applies `AnnotationRelations.Remove` for every relation of the group, and records a changed parent in `relations` and `updates` for `AddRelationUpdates`.
- `CosmosAnnotationService.Deletion.cs`, `RestoreCoreAsync` (lines 423–437): `outsideParents` are the parents of the written annotations that are neither written nor in `known` (the ancestors and the restored descendants), distinct by key text. The loop puts `await store.LoadAsync(…, withConfiguration: false, withAudit: false)` of each into `known`, one after another; `AddToParents` then skips the missing and deleted ones and adds the names to the active ones.
- `AnnotationItemStore.LoadAsync` with both flags off is one point read of the annotation item: a node with `Entity` and the `ETag` of the response, or a node without `Entity` (`Missing`) on `404`.
- `AnnotationItemStore.ReadNodesAsync(keys, withAudit, cancellationToken)` (lines 283–300, added for the bulk writer in 424-252) returns a node per key, by key text: it seeds a node for every distinct key and fills them from reads of many items in chunks of `ReadManyChunkSize` (1,000) through `LoadManyAsync` (lines 371–428). A key that isn't returned keeps a node without `Entity`, so it is `Missing`, as `LoadAsync` answers it. The `ETag` is the item's `_etag`, the value the response header of a point read carries. With `withAudit: false` it reads only the annotation items. No keys, no request. A failed read throws `CosmosRequestFailedException`, a `CosmosException`, which `BatchPlan` already throws for failed batches.
- `AnnotationItemStore.ReadManyAsync` (lines 239–281, used by `FindDeletedAnnotationsAsync`) returns only the found nodes, in the order of the answer, in one request without chunks.
- Both operations run in `RetryAsync`: a `412`, `409` or `404` of a batch (`AnnotationConcurrencyException`) runs the whole operation again, with fresh reads, up to three times.
- Tests: `Backend.CosmosDb/test/CosmosAnnotationLifecycleTests.cs` covers delete and restore over the shared service. `StorageInterceptor` and `InterceptedContainer` let a test run a service whose container records batches and calls hooks around point reads (`AfterRead`) and reads of many items (`BeforeReadMany`), but they record only the batches.

## Proposed Changes

### 1. The delete reads its outside parents at once

In `DeleteCoreAsync`, before the loop over `removals`:

```csharp
var parents = await store.ReadNodesAsync(
    removals.Select(group => FullKey.Create(group.First().Parent, fullKey)),
    withAudit: false);
```

The loop keeps its order and its body, and takes the node from `parents[group.Key]` instead of a `LoadAsync`. The group key is the parent's key text, the key of the dictionary. A missing or deleted parent is skipped as now, the removals of a group apply to the same entity, and `AddRelationUpdates` writes each changed parent with the `ETag` of this read. No outside parent, no request.

### 2. The restore reads its outside parents at once

In `RestoreCoreAsync`, the loop over `outsideParents` becomes one call whose nodes all go into `known`, the missing ones too:

```csharp
var outside = await store.ReadNodesAsync(outsideParents.Select(parent => FullKey.Create(parent, fullKey)), withAudit: false);

foreach (var (keyText, node) in outside)
{
    known[keyText] = node;
}
```

`outsideParents` and `AddToParents` stay as they are.

### 3. What stays

- No new helper: `ReadNodesAsync` already is the read the issue describes (a node per requested key, `Missing` for the ones not found, chunks of 1,000, the `_etag`). `ReadManyAsync` and `FindDeletedAnnotationsAsync` are unchanged.
- The reads stay inside the retried operation, so every attempt reads the parents again and writes under fresh ETags.
- Out of scope, as the issue says: `LoadNodesAsync` (the ancestors of a restore, which need their configurations and audits and are read in parallel already) and the point read in `CollectDeletedChildrenAsync` (the other parents of a deleted child of an annotation restored alone, only when they are neither known nor descendants). `CompleteParentRelationsAsync` (the completion of a create's relation arrays) reads its parents one after another too, but it is not part of a delete or a restore; it is left for a follow-up.
- No change to the API, the stored data, the batch plan, the waves or the audit.

### 4. Test instrumentation

`StorageInterceptor` records the reads besides the batches, so a test counts them without its own hooks:

- `PointReads`: the id of every point read through `ReadItemStreamAsync` (`InterceptedContainer` already reports them to `OnReadAsync`);
- `ReadsOfMany`: the ids of every read of many items through `ReadManyItemsStreamAsync`, one list per request, also when `BeforeReadMany` answers it;
- both in concurrent queues (the ancestors of a restore are read in parallel), cleared by `Reset`.

### 5. Tests

A new class `CosmosAnnotationParentReadTests` (`Backend.CosmosDb/test`) runs its own `CosmosAnnotationService` over an `InterceptedRepositoryProvider`, as `CosmosAnnotationConditionalWriteTests` does, with a project per test, and creates the data through the shared service, so only the operation under test is recorded:

1. **A delete reads its outside parents in one read of many items:** a context with two executions (`exe.<s>.<r1>.<c>`, `exe.<s>.<r2>.<c>`) is deleted. Its outside parents are the subject and the two usages, in two kinds of partitions. Exactly one read of many items, whose ids are the three parents; no point read of any of them; the subject and both usages lose the context's name.
2. **A restore reads its outside parents in one read of many items:** a usage with two executions in two contexts is deleted and restored with descendants. Exactly one read of many items, whose ids are the two contexts; no point read of them; both contexts get the responsibility's name back.
3. **A retried delete and a retried restore read the parents again:** the first batch of the operation loses to a change of its item (a patch through the shared container before the batch, so the real batch answers `412`). The operation succeeds on the second attempt, the read of many items of the parents ran twice, and the arrays are right.

In `CosmosAnnotationLifecycleTests`, the acceptance test of a deleted outside parent:

4. **A delete with a deleted (or expired) outside parent changes only the active one:** `uxe.<s>.<r>.<c>.<u>` is created, `sbt.<s>` deleted, and `cnt.<s>.<c>` restored with descendants: the execution and its unit of execution are active again while the usage stays deleted and keeps `<c>` in its `ContextNames`. Deleting the execution then has two outside parents: the deleted usage and the active context. The context loses `<r>`; the usage keeps its `ContextNames`, its `Deletion` and its `_etag` (it is not written). A second case removes the usage's item before the delete, as if its `ttl` had run out: the missing parent is skipped the same way.

The existing delete and restore tests stay as they are and must pass: `CosmosAnnotationLifecycleTests`, `CosmosAnnotationConditionalWriteTests`, and the other suites that delete and restore (`AnnotationIsolationTests`, `ConfigurationIsolationTests`, `CosmosConfigurationRestoreTests`, …), run with `STORYTELLER_TESTS_USE_CONTAINER=true`, in chunks by `--filter-class`.

### 6. Documentation

`docs/Platform/Storyteller/annotation-lifecycle.md`: step 3 of [Delete](../annotation-lifecycle.md#delete) and step 5 of [Restore](../annotation-lifecycle.md#restore) say that the outside parents are read in one read of many items and written with the ETag of that read.
