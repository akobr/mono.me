# Invalidate Descendant Caches on the First Configuration Write

Linear: [424-44 Fix first write into configuration which doesn't exist yet](https://linear.app/424fun/issue/424-44/fix-first-write-into-configuration-which-doesnt-exist-yet)

## Problem

A configuration read caches the effective document in `CalculatedContent` of the read item. A later write to an ancestor has to clear that cache on every descendant that merged the ancestor in. Otherwise a descendant keeps serving the old effective document.

The first write to an annotation with no configuration item yet skips this step. It creates the item and returns, so descendants that were calculated without this ancestor keep their cached document. They stay stale until another write clears them: a second write to the same ancestor, a write to another ancestor or to the descendant itself, or a template write.

Example: a context configuration is read and cached while its subject has no document. Then a first document is stored on the subject. Reads of the context keep returning the cached document without the subject's values.

The same create path also serves the import of a configuration whose key had no item (`ConfigurationImportOutcome.Created`), so an import has the same gap.

## Current State

- `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`
  - `CreateOrUpdateConfigurationAsync` (line 333) calls `WriteConfigurationAsync(key, value, actor, force, merge: true)`.
  - `WriteConfigurationAsync` (line 339), create branch (lines 355–385): when `existingConfiguration is null` it applies the `$remove`/`$patch` directives, validates against the schema, creates the item with `CreateItemAsync` (line 382), writes the audit entry and returns. It never calls `InvalidateConfigurationsAsync`.
  - Update branch of the same method: archives the previous version, then calls `InvalidateConfigurationsAsync(key, repository)` (line 451) *before* `UpsertItemAsync` (line 462). The comment at line 450 says "invalidate all ancestor configurations", but the queries select descendants.
  - `PatchConfigurationAsync` (line 468) also invalidates before its transactional batch. `ClearConfigurationAsync` (line 561) and `DeleteAsync` (line 608) invalidate *after* their write.
  - `ImportConfigurationAsync` (line 717) writes through `WriteConfigurationAsync(..., merge: false)` (line 796), so a `Created` import goes through the same create branch.
  - `InvalidateConfigurationsAsync` (line 1021) selects, per annotation type, the descendants whose effective document includes the written annotation, and clears their cache through `ConfigurationCacheInvalidator.InvalidateAsync`.
  - `CalculateAndCacheConfigurationAsync` (line 907) reads the item with its ETag, walks the ancestors, and caches the result only with `IfMatchEtag` set to that ETag (lines 958–970). A write to the item between the read and the cache, such as an invalidation, makes the replace fail with 412, and the result is returned without being cached.
- `src/Platform/Storyteller/Backend.CosmosDb/src/Configuring/ConfigurationCacheInvalidator.cs`: the invalidation patch sets `CalculatedContent` and `CalculatedContentHash` to null and increments `AffectedCounter`. The increment changes the ETag of every patched item, even one that had no cache.
- `docs/Platform/Storyteller/inheritance.md`, section "Cache and descendant freshness": documents the gap ("The first insert … leaves descendant caches as they are"), the misleading call-site comment, and claims that replacing a type template leaves `CalculatedContent` in place. That last claim is outdated: every template write invalidates, as `templating.md` says.
- `docs/Platform/Storyteller/templating.md`, section on cache invalidation: "The first insert of a configuration item, when none exists yet, returns after `CreateItemAsync` and leaves descendant caches as they are."
- `src/Platform/Storyteller/Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`: no test covers the cache of a descendant across an ancestor's first write.

## Proposed Changes

### 1. Invalidate descendants on the create path

In `WriteConfigurationAsync`, create branch, call `InvalidateConfigurationsAsync(key, repository)` right after `CreateItemAsync` succeeds and before the audit entry:

```csharp
await repository.Container.CreateItemAsync(configuration, partitionKey);

// invalidate the descendants after the document exists, so a calculation that missed it cannot be cached
await InvalidateConfigurationsAsync(key, repository);
await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, configuration.Version);
```

**Order: after the create, not before.** A concurrent read of a descendant can read the descendant item with its ETag, then read this ancestor before the create, and then try to cache the result.

- If the invalidation runs *after* the create, that calculation is either cached before the invalidation, which then clears it, or cached after, which fails on the ETag that the invalidation changed. No stale cache survives.
- If the invalidation ran *before* the create, a calculation that starts after a descendant was patched and reads the ancestor before the create would be cached with the old effective document. For a responsibility, which invalidates a whole partition in batches, that window can be long.

The order after the write is also what `ClearConfigurationAsync`, `DeleteAsync` and the template writes use.

Consequences:

- For a responsibility, the predicate matches every configuration item in the partition, including the item just created. That patch sets fields that are already null and increments `AffectedCounter`. The returned `Configuration` is built from the local entity, so it is unaffected.
- The import (`ImportConfigurationAsync` → `WriteConfigurationAsync(merge: false)`) gets the same fix, with no change of its own.
- A failed invalidation after the create surfaces as an error (`ConfigurationStorageException`) after the document is stored. A retry with the same document then takes the no-change path and does not invalidate. The same is true of `ClearConfigurationAsync` today. This is accepted and documented, not changed here.
- The cost is one invalidation per first write. It is the same query and patch that a second write already runs.

### 2. Correct the call-site comment

The comment above the update branch's `InvalidateConfigurationsAsync` call (line 450) becomes "invalidate the cached calculations of all descendants". The docs then no longer need to warn about it.

### 3. Tests

In `src/Platform/Storyteller/Backend.CosmosDb/test/CosmosConfigurationServiceTests.cs`, against the Cosmos DB emulator like the rest of the suite:

- `FirstWrite_OfAnAncestor_InvalidatesTheCachedDescendant` — a `[Theory]` over the ancestor types Responsibility, Unit, Subject, Usage, Context and Execution, each with its own annotation names. It creates a unit-of-execution tree, stores a document on the unit of execution only, and reads it so that its calculation is cached. Then it makes the first write to the ancestor and checks that:
  - the unit-of-execution item no longer has a cached calculation;
  - the next read contains the ancestor's value.

  The unit of execution is a descendant of every other type, so each branch of `InvalidateConfigurationsAsync` that has descendants is covered.
- `FirstWrite_OfAnAncestor_InvalidatesACachedInheritedDocument` — the descendant has no document of its own. A usage inherits a document from its subject, and its read caches it in a new empty item. The first write to the responsibility then has to clear that item, and the next read of the usage contains both values.
- `Import_CreatingAnAncestorsDocument_InvalidatesTheCachedDescendant` — the same check through `ImportConfigurationAsync` with outcome `Created`.

Each test fails without the change in section 1.

### 4. Documentation

- `docs/Platform/Storyteller/inheritance.md`, "Cache and descendant freshness":
  - the first insert invalidates descendants after the item is created, with the reason for the order;
  - drop the paragraph about the misleading call-site comment;
  - replace the outdated sentence about type-template writes with a pointer to `templating.md`, which describes their invalidation.
- `docs/Platform/Storyteller/templating.md`: the list of writes that clear cached calculations names the first insert too, and the sentence saying that it leaves descendant caches as they are is replaced.

### Out of scope

- The update branch of `WriteConfigurationAsync` and `PatchConfigurationAsync` invalidate *before* their write, which leaves the window described in section 1. Moving them is a separate change, because there the order also decides what a failed invalidation leaves behind for a retry.
- A descendant item that a concurrent calculation creates after the invalidation query has run is not reached by any invalidation. This holds for every write path. Templates recheck their ETags after such a create, but ancestors do not.
