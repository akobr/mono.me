# Phase A of Versioning of Configuration Templates

## Overview

Phase A of [2026-09-28 Versioning of Configuration Templates](../2026-09-28%20Versioning%20of%20Configuration%20Templates.md) moves configuration templates to a view-level identity: one template per annotation type per project and view, stored in a dedicated project partition like type-level schemas, with views fully isolated. It adds versioning with history modelled on configuration versioning, a template service, and HTTP endpoints. It also clears cached calculations whenever a template changes. The diff engine and cache invalidation were extracted from `CosmosConfigurationService` into shared helpers. The shared invalidation helper now respects the Cosmos DB limit of 100 operations per transactional batch. Phase B (SDK regeneration and CLI) is not part of this review.

All 104 tests in `Backend.CosmosDb.UnitTests` pass, including the 17 template tests (see section 8).

### Revision 2026-09-30: templates per view

The first implementation (commit `956060d`) made templates project-wide: one `gen.{typeCode}` per project, applied to every view. A write therefore invalidated configurations across the whole project. The spec was revised (decision D2), and the implementation now scopes templates to **project + view**:

- Ids are `{view}.gen.{typeCode}` and `{view}.gnv.{typeCode}.{version}` in `{project}.template`, and `ViewName` is set.
- Every service method and every HTTP route takes the view.
- Invalidation covers the template's view only.

The sections below describe the revised state.

## What Was Done

### 1. Template identity and storage

- `PartitionKeys` (`Backend.CosmosDb/src/PartitionKeys.cs`): new constant `TemplatePartitionSuffix = "template"` and new methods `GetTemplate(project)` → `{project}.template` and `GetCosmosTemplate(project)`.
- `EntityIdPrefixTypes`: new values `GenerateTemplate = "gen"` and `GenerateTemplateVersion = "gnv"`.
- A template item is `{view}.gen.{typeCode}` in `{project}.template`, with `ViewName = view` and `AnnotationKey = Name = typeCode`. All views of a project share the partition, and the view prefix in the id separates them, as it does for configuration ids. Type codes are lower-cased and must be in `AnnotationTypeCodes.ValidCodes`. `CosmosConfigurationTemplateService.GetTemplateId(view, type)` builds the id and is shared with the calculation path.

### 2. Entities

- `GenerateTemplateEntity` gets `Version` (`ulong`).
- New `GenerateTemplateHistoryEntity` (`Entities/Configurations/`), the same shape as `ConfigurationHistoryEntity`, with a 365-day `ttl`. History ids are `{view}.gnv.{typeCode}.{version}`, so each view has its own version sequence. `TemplateMappings.ToHistory` takes the view from the entity.
- `ConfigurationHistoryEntityExtensions` has a second `GetExpirationTime` overload for the template history entity. Both overloads share one private calculation.
- Deviation: the spec's first draft had a `GetEffectiveVersion()` for items without `Version`. The final spec dropped it, because old-location items are no longer read (decision D1), so it was not implemented.

### 3. Calculation read path

- `CosmosConfigurationService.TryAutogeneratePropertiesAsync` now takes the node `FullKey` and reads `{key.ViewName}.gen.{typeCode}` from `PartitionKeys.GetCosmosTemplate(key.ProjectName)`. The type code is lower-cased before the lookup.
- `CalculateAndCacheConfigurationAsync` takes a `Dictionary<string, JObject?>` memo. `GetRawConfigurationAsync` creates the memo per calculation and passes it down the recursion. All nodes of the graph share the view, so each type's template, including "no template", is read at most once per calculation. Nothing is cached across requests.
- Old-location items (`{view}.gen.{type}` inside annotation partitions) are ignored, as decision D1 requires. They have the same id as a new template, but they live in a different partition. The test `LegacyTemplateInAnnotationPartition_IsIgnored` covers this.

### 4. Models

- New `ConfigurationTemplate` (`Abstractions.Annotations/src/Model/`) with `AnnotationType`, `Version`, `Content`, and `Author`. The mapping takes `AnnotationType` from the entity `Name`.
- The version list reuses `ConfigurationVersion`, and diffs reuse `DiffResult`.

### 5. Service

- `IConfigurationTemplateService` (`Backend.Core/src/Configuring/`) has the eight methods from the spec, each taking `(organization, project, view, annotationType, …)`.
- `TemplateNotFoundException(project, view, annotationType)` is in the same folder.
- `CosmosConfigurationTemplateService` (`Backend.CosmosDb/src/Configuring/`) is registered as a singleton in `EntryPoint.AddCosmosDbAnnotations`. Behaviour:
  - **Create** — `RemoveRequested` and `ApplyPatchRequested`, then `Version = max(history) + 1`, then `CreateItemAsync`. If another writer created the item first (409), the call is retried as an update.
  - **Update and patch** — merge or JSON Patch. Equal content is a no-op. Otherwise one transactional batch runs `CreateItem(history)` and `ReplaceItem(..., IfMatchEtag)`.
  - **Delete** — one batch with `CreateItem(history)` and `DeleteItem(..., IfMatchEtag)`.
  - **Retries** — a batch that fails with 409 (the version was already archived), 412 (ETag mismatch), or 404 (the item was deleted) re-reads and retries, up to 3 retries. The retry policy matches `UpsertWithOptimisticConcurrencyAsync`. Any other batch failure throws `InvalidOperationException`. The caller's input `JObject` is cloned on each attempt, because `RemoveRequested` mutates it.
  - **Invalidation** — every successful write invalidates caches (section 6) **after** the template write.
  - **Reads and diffs** — mirror the configuration methods; version `0` is an empty object.
- Deviation: an unknown type code throws `ArgumentOutOfRangeException` from the service. The API validates the type code before it calls the service, so HTTP callers get 400.
- New `TemplateMappings` (`Backend.CosmosDb/src/`) provides `ToConfigurationTemplate` and `ToConfigurationVersion` for both entities, and `ToHistory` to build the history item.

**Extracted helpers** (behaviour-preserving):

- `JsonContentDiffer` (`Configuring/JsonContentDiffer.cs`) is `GetConfigurationChangesAsync`, `ComputeWordSegments`, `BuildSegments`, and `BuildHunks`, moved verbatim. The entry point is renamed to `GetChangesAsync`. `CosmosConfigurationService` now creates one in its constructor and delegates to it, and the `DiffPlex` usings were removed from the service.
- `ConfigurationCacheInvalidator` (`Configuring/ConfigurationCacheInvalidator.cs`) replaces the two private static `InvalidateConfigurationsAsync` overloads. The seven call sites in `CosmosConfigurationService.InvalidateConfigurationsAsync(FullKey, …)` now call `ConfigurationCacheInvalidator.InvalidateAsync`. The per-type switch did not change.

### 6. Cache invalidation

- `ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, view, typeCode)` looks up the affected types in a static table (the spec table). It queries configuration items across partitions with `ProjectName == project`, `ViewName == view`, `Id` starting with `{view}.cnf.`, and `AnnotationKey` starting with one of the affected codes. The predicate is built as an expression tree `OR` chain, and the query goes through the shared cross-partition `InvalidateAsync`.
- Other views are never touched.
- The project-wide first version matched ids by `Contains(".cnf.")` and needed a client-side id check to guard against annotation names containing `cnf`. With the view known, the `StartsWith` prefix is exact, so that check and its projection type were removed.
- Batch-limit fix, shared with configuration invalidation:
  - Ids are read through a paged `FeedIterator`. The cross-partition overload used a synchronous `.ToList()` before.
  - Each partition is patched in chunks of `MaxBatchOperations = 100`, with at most 8 batches in parallel.
  - A failed batch now throws `InvalidOperationException`. Before, failures were ignored.
  - If a batch fails because an item was deleted in the meantime (404), that chunk falls back to patching items one by one and skips the deleted ones.
- **Deviation / additional fix:** invalidation now **sets** `CalculatedContent` and `CalculatedContentHash` to `null` instead of **removing** them. Cosmos DB rejects a `Remove` patch on a missing path. The serializer uses `NullValueHandling.Ignore`, so any item that was already invalidated or never calculated has no `CalculatedContent` property. With `Remove`, one such item made the whole transactional batch fail, and because the result was not checked, nothing in that partition was invalidated. Setting `null` is idempotent and reads back as `null`, so the cache check (`CalculatedContent is not null`) behaves the same. This also changes behaviour for configuration writes: invalidations that silently failed before now apply.

### 7. API

- `Definitions.cs`:
  - Routes: `Routes.Template.V1` with `Template`, `Versions`, `Version`, `VersionDiff`, `VersionDiffCustom`, under `v1/{organization}/{project}/{view}/template/{annotationType}`. The routes include the view, like configuration routes.
  - Route ids: `RouteIds.Template` has the eight operations.
  - Tag: `Tags.Template`.
- New `TemplateHttp` (`Api.Functions/src/V1/TemplateHttp.cs`) has the eight functions, each with a `view` path parameter. Reads use the configuration and default read/write scopes with project access. Writes use `Configuration.Write` or `Default.Write` and `AccountRole.Contributor`. Status codes:
  - **Set:** invalid JSON or `InvalidOperationException` → 400.
  - **Patch:** `TemplateNotFoundException` → 404, `InvalidOperationException` → 400.
  - **Delete:** `false` → 404.
- New shared helpers in `Api.Functions/src/V1/`:
  - `AnnotationTypeValidation.TryValidate` — `ConfigurationSchemaHttp.TryValidateAnnotationType` now delegates to it.
  - `DiffResultHttpExtensions.ToDiffResponse` — `FormatDiffResult`, moved.
  - `DiffResultHttpExtensions.ToDiffResponseAsync` — awaits the diff and maps `InvalidOperationException` to 404 with an `ErrorResponse`.
- The three configuration diff endpoints (`GetConfigurationVersionDiff`, `GetConfigurationVersionDiffCustom`, `GetConfigurationViewDiff`) now use `ToDiffResponseAsync`. An unknown version or view returns 404 instead of 500. Their OpenAPI attributes still declare 404 without a body; they were left unchanged so the generated SDK stays the same.
- `DiffUnifiedResponseDocumentFilter` now also lists `GetTemplateVersionDiff` and `GetTemplateVersionDiffCustom`.

### 8. Tests

`Backend.CosmosDb/test/CosmosConfigurationTemplateServiceTests.cs` contains 17 tests. Each test uses its own project, so templates of different tests never interfere:

- `NonExistingTemplate`, `VersioningOfTemplate`, `UpdateWithSameContent_DoesNotCreateVersion`
- `TypeCode_IsCaseInsensitive`, `UnknownTypeCode_Throws`
- `PatchTemplate_CreatesVersionHistory`, `PatchTemplate_Missing_Throws`
- `DeleteThenCreate_ContinuesVersionNumbering`, `ConcurrentTemplateWrites_ProduceConsecutiveVersions`
- `Template_AppliesToAllPartitionsInView`, `TemplateMergeOrder_StoredContentWins` (the worked example from `templating.md`)
- `Templates_AreIsolatedPerView`: a template in one view is not merged in another view. Each view has its own content and version sequence. A write in one view leaves the cached calculations of the other view intact.
- `TemplateWrite_InvalidatesCalculatedConfiguration`, `SubjectTemplateWrite_InvalidatesDescendantsAcrossPartitions`, `TemplateWrite_DoesNotInvalidateUnrelatedTypes`
- `Invalidation_MoreThan100ConfigurationsInPartition` (105 units in one responsibility partition)
- `LegacyTemplateInAnnotationPartition_IsIgnored`

The spec's `InvalidAnchorOrType_IsRejected` and `CreateTemplate_MissingAnchor_Throws` are gone, because the final spec has no anchors. `UnknownTypeCode_Throws` replaces them.

**Results.** `dotnet test src/Platform/Storyteller/Backend.CosmosDb/test` ran against the local Cosmos DB emulator. The run wiped the emulator databases, which the user approved. **104 of 104 tests pass** after the per-view revision, including the existing `CosmosConfigurationServiceTests`, after the extractions and the set-null invalidation change. `Invalidation_MoreThan100ConfigurationsInPartition` takes about 30 s because it creates 105 annotations.

**Fix found by the tests: `_ts` was never read.** In the first run, `VersioningOfTemplate` failed: the history expiration came back as 1971-01-01. `Entity.LastUpdatedEpochTimestamp` (`_ts`) was get-only (`{ get; }`), so Newtonsoft never set it when deserializing, and it was always `0`. This bug already existed and affected configurations too:

- Every `ConfigurationHistoryEntity.CreationTime` was stored as 1970-01-01.
- The current version's `CreationTime` was 1970-01-01.
- Every history `ExpirationTime` was 1971-01-01.

The property is now `{ get; init; }` (`Backend.CosmosDb/src/Entities/Entity.cs`). Writes are unaffected, because the getter was already serialized and Cosmos DB sets `_ts` itself. History items written before this fix keep their stored 1970 `CreationTime`; only their `ExpirationTime`, which is computed from `_ts` on read, is correct now. `VersioningOfConfiguration` gained assertions on `CreationTime` and `ExpirationTime` to guard against a regression.

### 9. Documentation

- `docs/Platform/Storyteller/templating.md`:
  - Rewritten intro, *Overview*, *Document*, and *Identity* for the view-level identity (`{view}.gen.{typeCode}` in `{project}.template`).
  - New sections: *Earlier storage location*, *Managing templates* (service and HTTP API), and *Versions*.
  - *Cache* updated with the template invalidation table (the template's view only), batching and failure behaviour, and a note that templates written directly into Cosmos are not invalidated.
  - The worked example now refers to `default.gen.exe` of the project `billing`.
- `docs/Platform/Storyteller/inheritance.md`: step 5 of the calculation now points at the view template.

### Build

- `Backend.CosmosDb` and `Backend.CosmosDb.UnitTests` build with no new warnings. Remaining warnings in `CosmosConfigurationService.cs` (CS8625, SA1515, CA2208, CS8602) were already there.
- `Api.Functions` builds with no new warnings. It was built with `-p:OutDir` to a temporary folder, because the running `Api.Functions` process locks `bin/Debug`.
