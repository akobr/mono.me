# Invalidate Descendant Caches on the First Configuration Write

## Overview

The first write to an annotation that had no configuration item now invalidates the cached calculations of its descendants, so they no longer keep an effective document computed without it. This covers both `CreateOrUpdateConfigurationAsync` and an import that creates the item. The review implements [Invalidate Descendant Caches on the First Configuration Write](../2026-10-17%20Invalidate%20Descendant%20Caches%20on%20the%20First%20Configuration%20Write.md) in full, in one phase. It also corrects two statements of the spec that turned out to be too broad (see 1 and 4). Linear: [424-44](https://linear.app/424fun/issue/424-44/fix-first-write-into-configuration-which-doesnt-exist-yet).

## What Was Done

### 1. Invalidate descendants on the create path

- `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`, `WriteConfigurationAsync`, create branch: `InvalidateConfigurationsAsync(key, repository)` runs right after `CreateItemAsync` and before the audit entry (line 386), as planned. No other code changed.
- The order is after the create, for the reason in the spec: the invalidation patch changes the ETag of every descendant item, and `CalculateAndCacheConfigurationAsync` caches only on the ETag it read before the walk. A calculation that read the new document as missing is either cleared or not cached.
- `ImportConfigurationAsync` gets the fix through the same branch, with no change of its own.

Two corrections to the spec, both found in code review:

- **Not every `Created` import takes the create branch.** The spec says an import with the outcome `Created` goes through it. An import reports `Created` whenever the item has no stored `Content`. That includes a system cache row: an item with empty `Content` that a calculation created to cache an inherited document. Such an item exists, so the write takes the existing-item branch. The same goes for a `CreateOrUpdateConfigurationAsync` over it. That branch already invalidated before this change, so these writes were never stale in the way the issue describes. Its order, invalidate before the write, is the one listed under "Out of scope". The docs now say this.
- **"No stale cache survives" holds for each item, not for the whole invalidation.** `ConfigurationCacheInvalidator` patches in transactional batches of at most 100 items per partition, up to 8 at a time, in no fixed order. When a partition has more than 100 affected items, a descendant cleared in an early batch can be recalculated from an intermediate descendant whose stale cache sits in a later batch. For example, an execution can be recalculated from its usage before the usage is cleared, and the stale result caches successfully on the execution's new ETag. This predates the change and affects every write that invalidates, template writes included. It is documented and left for a follow-up.

### 2. Correct the call-site comment

The comment on the update branch's call (line 454) now reads "invalidate the cached calculations of all descendants". The paragraph in `inheritance.md` that warned about the old comment is gone.

### 3. Tests

In `Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`, in the new project `config-first-write`. The project has no templates, so a read leaves no item behind for an ancestor without a document.

- `FirstWrite_OfAnAncestor_InvalidatesTheCachedDescendant`: a `[Theory]` over Responsibility, Unit, Subject, Usage, Context and Execution, each with its own tree. The unit of execution has a document and a cached calculation. A precondition checks that the ancestor has no configuration item, so the write really takes the create branch. After that write, the unit of execution has no cache, and its next read contains the ancestor's value and keeps its own.
- `FirstWrite_OfAnAncestor_InvalidatesACachedInheritedDocument`: a usage without a document of its own caches the subject's document in a system cache row. The responsibility's first write clears it, and the next read merges both.
- `Import_CreatingAnAncestorsDocument_InvalidatesTheCachedDescendant`: an import with the outcome `Created` on a subject without an item clears the cached context below it. The precondition that the subject has no item was added after review: `Created` alone cannot show which branch ran.
- The new helpers `HasConfigurationItemAsync` and `HasCachedCalculationAsync` read the raw items, like the template tests do.

All 8 cases failed before the fix ("Expected HasCachedCalculationAsync(...) to be False, but found True") and pass with it. The whole `Backend.CosmosDb` suite passes: 362 tests, against the local Cosmos DB emulator.

### 4. Documentation

- `docs/Platform/Storyteller/inheritance.md`, "Cache and descendant freshness":
  - The first insert invalidates after the item is created, and the section explains why. An import of a key without an item takes the same path.
  - A first document written over a system cache row takes the existing-item path, which invalidates before it writes. This is the correction from section 1.
  - The guarantee is stated per item. The two gaps that remain on every write are listed: the unordered batches, and a descendant item created after the invalidation query. This is also a correction from section 1.
  - The stale sentence about type-template writes is replaced. Writes through the service invalidate; a template item written directly into Cosmos does not. It links to `templating.md#cache`.
  - Two existing sentences put the target before the descendants. The update and patch paths invalidate the descendants first, so those sentences now say so.
  - The paragraph about the misleading comment is removed.
- `docs/Platform/Storyteller/templating.md`, "Cache": the list of writes that clear caches names the first insert and the import. The sentence that said the first insert leaves descendant caches as they are now points to `inheritance.md`.
- No page in `docs/42for.net` described this behaviour, so the public site needed no change.

### Code review

Three reviewers looked at the change, one each for correctness and concurrency, tests, and docs. Two skeptics then tried to refute every finding.

- **Upheld:** the two spec corrections in section 1.
- **Refuted:**
  - An audit entry is lost when the invalidation fails after the create. The order matches `ClearConfigurationAsync` and `DeleteAsync`, and the audit is documented as best effort.
  - The import test asserts only `Created`. It still exercises the create branch; the precondition was added anyway.
  - The wording of the order in the existing section. That sentence predates the change and was corrected anyway.

### Follow-ups found during the work, not changed

Each was confirmed against the code.

1. **A failed invalidation wedges a configuration.**
   - The update branch of `WriteConfigurationAsync` creates the history item of the current version, then invalidates, then upserts. If the invalidation throws, the history item exists but the version is unchanged.
   - Every later `CreateOrUpdateConfigurationAsync` or import then fails with a 409 from Cosmos on the same history id, which is reported as 500.
   - `PatchConfigurationAsync` returns `ConflictException` ("changed concurrently, try again") every time.
   - `ClearConfigurationAsync` and `ForceDeleteConfigurationAsync` don't check their batch response. They report success while the document stays.
   - Only a soft delete and restore, or a re-creation of the annotation, clears it.
2. **The update and patch paths invalidate before their write.**
   - A calculation can read the descendant after it was patched, and read the ancestor before the write.
   - It then caches the old document on the descendant's new ETag.
   - Moving the invalidation after the write would close this window, but it interacts with follow-up 1.
3. **Unordered invalidation batches**, as described in section 1. Patching level by level (units, usages and contexts, then executions, then units of execution), with each level awaited before the next, would close it.
