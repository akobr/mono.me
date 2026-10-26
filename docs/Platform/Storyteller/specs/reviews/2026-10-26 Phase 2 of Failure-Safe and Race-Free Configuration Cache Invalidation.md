# Phase 2 of Failure-Safe and Race-Free Configuration Cache Invalidation

## Overview

The cache invalidation now runs level by level along the ancestor graph, and it queries a level only after the levels above it are patched (problem 3 of the spec). A calculation writes a cache only under an ETag that the item had before every read the calculation made. It reads the template on every visit of a node, and it creates a missing item without a cache before it calculates again. The template recheck after a create is gone, and the late-created descendant gap is closed. This review covers phase 2 of [Failure-Safe and Race-Free Configuration Cache Invalidation](../2026-10-26%20Failure-Safe%20and%20Race-Free%20Configuration%20Cache%20Invalidation.md), commit `07b61dce`, and the phase-2 part of the code-review fixes in the follow-up commit. Linear: [424-50](https://linear.app/424fun/issue/424-50/make-configuration-cache-invalidation-failure-safe-and-race-free).

## What Was Done

### 2.1 `ConfigurationCacheInvalidator`

- `Configuring/InvalidationTarget.cs`: `InvalidationTarget(TypeCode, Predicate, PartitionKey?)`, where `null` means a cross-partition query. This is a file of its own instead of a nested record.
- `InvalidateAsync(repository, targets)` replaces both overloads.
  - It groups the targets by level: 0 = `rst`, `sbt`; 1 = `unt`, `usg`, `cnt`; 2 = `exe`; 3 = `uxe`.
  - For each level, `CollectAsync` gathers the ids by partition. They are then patched with the existing chunks of 100, parallelism of 8, patch content and 404 fallback.
  - Each level is awaited before the next one is queried. A failure stops before the next level.
- `InvalidateForTemplateAsync` builds one target per type of `TemplateAffectedTypeCodes`. `CosmosConfigurationTemplateService` is unchanged.

### 2.2 Targets of `InvalidateConfigurationsAsync`

`GetDescendantTargets` returns the targets of the spec's table. Every predicate keeps the project, the view and the id prefix of the old ones. The Subject write is now one call with four targets. The written item is no longer a target. The code review compared every target with the old predicates and found no other difference.

### 2.3 Guarded cache writes

`CalculateAndCacheConfigurationAsync(node, repository)` lost its template dictionary. It is split into the following parts:

- `CalculateConfigurationAsync`: ancestors, then the template, then the own content.
- `TryCacheConfigurationAsync`: a replace guarded by the given ETag. `412` and `404` are ignored.
- `TryAutogeneratePropertiesAsync`: reads the template on every call.

A missing item with a non-empty result is created without a cache (`Content = {}`, version 0, stamped by `System`). The calculation then runs again and is cached under the created ETag. On a `409`, the other item is kept. An empty result creates nothing, so the first-write and composition tests that count rows are unchanged.

`HaveTemplatesChangedAsync` and `TemplateSnapshot` were removed. Their comment in the composition read ("as in the Effective calculation") was corrected after the code review.

### 2.4 Why no stale cache survives a completed write

The argument is in `inheritance.md`, with four limits:

- stale reads while an invalidation runs or stays pending;
- a pending invalidation is finished only by a write request;
- direct Cosmos writes bypass the invalidation;
- an older build drops the token during a deployment overlap.

The code review found that the consistency limit has to cover the point reads of a calculation too, not only the invalidation's queries. Under Session consistency, another client can read an ancestor from a replica that is behind. The doc now says so.

### 2.5 Tests

`CosmosConfigurationInvalidationOrderTests`, one project per test:

- `ResponsibilityWrite_InvalidatesLevelByLevel`
  - Setup: 105 cached execution rows inserted raw into the responsibility partition (more than one batch), plus a real unit-of-execution tree.
  - Every batch holds a single level and ends before the next level starts. The responsibility's own row is in no batch.
  - The raw rows are cleared, and the unit of execution reads the new value.
- `ItemCreatedDuringTheInvalidation_IsReachedByALaterLevel`
  - Added after the code review.
  - A cached execution inserted while level 1 is patched is cleared by level 2. That checks the late enumeration, not only the batch order.
- `ResponsibilityTemplateWrite_InvalidatesLevelByLevel`: the same order through the template service, starting with the responsibility's own row at level 0.
- `AncestorWritten_DuringTheCreateOfARow_IsNotCachedOld`: the responsibility is written right after the unit's calculation read it. The created unit row ends with the new value.
- `TemplateWritten_BetweenTwoVisitsOfARow_IsNotCachedOld`: the responsibility template is written after the first visit read it. The responsibility row is not cached with the old template.

With the source of phase 1 and these tests, all four tests that existed before the code review failed for the reason they test:

- mixed-level batches;
- `a` read as 1 on the created row;
- the old template cached on the responsibility row.

They pass with phase 2. The existing invalidation tests (`Invalidation_MoreThan100ConfigurationsInPartition`, `SubjectTemplateWrite_InvalidatesDescendantsAcrossPartitions`, the first-write theory and the composition tests) pass unchanged. Suite results are in the phase 1 review.

### 2.6 Documentation

- `inheritance.md`:
  - Step 5 of the calculation: the template is read on every calculation.
  - Step 7: the create first, then the guarded recalculation.
  - The descendant table has levels, followed by the level order.
  - The two old gaps are replaced by the argument and its limits.
- `templating.md`, "Cache":
  - the created item and the guarded recalculation;
  - the template read on every visit;
  - the level order of the invalidation;
  - a responsibility's own item losing its cache with the write;
  - the pointer to `GetDescendantTargets`.

### Code review

Findings upheld for phase 2, all low:

- the composition comment;
- the consistency limit in `inheritance.md`;
- the missing test for the late enumeration.

All three were fixed in the follow-up commit. Findings refuted:

- the unused template ETag of `ReadTemplateAsync`;
- the never-null `Task<JObject?>`. Both are harmless, and the second already existed.

### Left for later

- The emulator cannot show a stale read from a lagging replica. The consistency limit stays documented.
- Merging the two history queries of a first write (the highest version and the pending clears) was left out on purpose. Its cost is noted in `audit-operations.md`.
