# Phase A of Versioning of Configuration Templates

## Overview

Phase A of [2026-09-28 Versioning of Configuration Templates](../2026-09-28%20Versioning%20of%20Configuration%20Templates.md) moves configuration templates to a project-level identity (one template per annotation type per project, for all views, stored like type-level schemas). It adds versioning with history modelled on configuration versioning, a template service, and HTTP endpoints. It also clears cached calculations whenever a template changes. The diff engine and cache invalidation were extracted from `CosmosConfigurationService` into shared helpers. The shared invalidation helper now respects the Cosmos DB limit of 100 operations per transactional batch. Phase B (SDK regeneration and CLI) is not part of this review.

All 103 tests in `Backend.CosmosDb.UnitTests` pass, including the 16 new template tests (see section 8).

## What Was Done

### 1. Template identity and storage

- `PartitionKeys` (`Backend.CosmosDb/src/PartitionKeys.cs`): new constant `TemplatePartitionSuffix = "template"` and new methods `GetTemplate(project)` → `{project}.template` and `GetCosmosTemplate(project)`.
- `EntityIdPrefixTypes`: new values `GenerateTemplate = "gen"` and `GenerateTemplateVersion = "gnv"`.
- A template item is `gen.{typeCode}` in `{project}.template`, with `ViewName = ""` and `AnnotationKey = Name = typeCode`. Type codes are lower-cased and must be in `AnnotationTypeCodes.ValidCodes`. `CosmosConfigurationTemplateService.GetTemplateId` builds the id and is shared with the calculation path.

### 2. Entities

- `GenerateTemplateEntity` gets `Version` (`ulong`).
- New `GenerateTemplateHistoryEntity` (`Entities/Configurations/`), the same shape as `ConfigurationHistoryEntity`, with a 365-day `ttl`. History ids are `gnv.{typeCode}.{version}`.
- `ConfigurationHistoryEntityExtensions` has a second `GetExpirationTime` overload for the template history entity. Both overloads share one private calculation.
- Deviation: the spec's first draft had a `GetEffectiveVersion()` for items without `Version`. The final spec dropped it, because old-location items are no longer read (decision D1), so it was not implemented.

### 3. Calculation read path

- `CosmosConfigurationService.TryAutogeneratePropertiesAsync` now takes the node `FullKey` and reads `gen.{typeCode}` from `PartitionKeys.GetCosmosTemplate(key.ProjectName)`. The type code is lower-cased before the lookup.
- `CalculateAndCacheConfigurationAsync` takes a `Dictionary<string, JObject?>` memo. `GetRawConfigurationAsync` creates the memo per calculation and passes it down the recursion. Each type's template, including "no template", is read at most once per calculation. Nothing is cached across requests.
- Old-location items (`{view}.gen.{type}` inside annotation partitions) are ignored, as decision D1 requires. The test `LegacyTemplateInAnnotationPartition_IsIgnored` covers this.

### 4. Models

- New `ConfigurationTemplate` (`Abstractions.Annotations/src/Model/`) with `AnnotationType`, `Version`, `Content`, and `Author`. The mapping takes `AnnotationType` from the entity `Name`.
- The version list reuses `ConfigurationVersion`, and diffs reuse `DiffResult`.

### 5. Service

- `IConfigurationTemplateService` (`Backend.Core/src/Configuring/`) has the eight methods from the spec.
- `TemplateNotFoundException(project, annotationType)` is in the same folder.
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

- `ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, typeCode)` looks up the affected types in a static table (the spec table). It queries configuration items across partitions with `ProjectName == project`, `Id` containing `.cnf.`, and `AnnotationKey` starting with one of the affected codes. The predicate is built as an expression tree `OR` chain.
- Deviation: the query also returns `ViewName` and `AnnotationKey`, and a client-side check keeps only items whose id is exactly `{ViewName}.cnf.{AnnotationKey}`. An annotation named `cnf` would otherwise make `.cnf.` appear in unrelated ids.
- Batch-limit fix, shared with configuration invalidation:
  - Ids are read through a paged `FeedIterator`. The cross-partition overload used a synchronous `.ToList()` before.
  - Each partition is patched in chunks of `MaxBatchOperations = 100`, with at most 8 batches in parallel.
  - A failed batch now throws `InvalidOperationException`. Before, failures were ignored.
  - If a batch fails because an item was deleted in the meantime (404), that chunk falls back to patching items one by one and skips the deleted ones.
- **Deviation / additional fix:** invalidation now **sets** `CalculatedContent` and `CalculatedContentHash` to `null` instead of **removing** them. Cosmos DB rejects a `Remove` patch on a missing path. The serializer uses `NullValueHandling.Ignore`, so any item that was already invalidated or never calculated has no `CalculatedContent` property. With `Remove`, one such item made the whole transactional batch fail, and because the result was not checked, nothing in that partition was invalidated. Setting `null` is idempotent and reads back as `null`, so the cache check (`CalculatedContent is not null`) behaves the same. This also changes behaviour for configuration writes: invalidations that silently failed before now apply.

### 7. API

- `Definitions.cs`:
  - Routes: `Routes.Template.V1` with `Template`, `Versions`, `Version`, `VersionDiff`, `VersionDiffCustom`, under `v1/{organization}/{project}/template/{annotationType}`.
  - Route ids: `RouteIds.Template` has the eight operations.
  - Tag: `Tags.Template`.
- New `TemplateHttp` (`Api.Functions/src/V1/TemplateHttp.cs`) has the eight functions. Reads use the configuration and default read/write scopes with project access. Writes use `Configuration.Write` or `Default.Write` and `AccountRole.Contributor`. Status codes:
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

`Backend.CosmosDb/test/CosmosConfigurationTemplateServiceTests.cs` contains 16 tests. Each test uses its own project, because templates are project-wide:

- `NonExistingTemplate`, `VersioningOfTemplate`, `UpdateWithSameContent_DoesNotCreateVersion`
- `TypeCode_IsCaseInsensitive`, `UnknownTypeCode_Throws`
- `PatchTemplate_CreatesVersionHistory`, `PatchTemplate_Missing_Throws`
- `DeleteThenCreate_ContinuesVersionNumbering`, `ConcurrentTemplateWrites_ProduceConsecutiveVersions`
- `Template_AppliesToAllPartitionsAndViews`, `TemplateMergeOrder_StoredContentWins` (the worked example from `templating.md`)
- `TemplateWrite_InvalidatesCalculatedConfiguration`, `SubjectTemplateWrite_InvalidatesDescendantsAcrossPartitions`, `TemplateWrite_DoesNotInvalidateUnrelatedTypes`
- `Invalidation_MoreThan100ConfigurationsInPartition` (105 units in one responsibility partition)
- `LegacyTemplateInAnnotationPartition_IsIgnored`

The spec's `InvalidAnchorOrType_IsRejected` and `CreateTemplate_MissingAnchor_Throws` are gone, because the final spec has no anchors. `UnknownTypeCode_Throws` replaces them.

**Results.** `dotnet test src/Platform/Storyteller/Backend.CosmosDb/test` ran against the local Cosmos DB emulator. The run wiped the emulator databases, which the user approved. **103 of 103 tests pass**, including the existing `CosmosConfigurationServiceTests`, after the extractions and the set-null invalidation change. `Invalidation_MoreThan100ConfigurationsInPartition` takes about 30 s because it creates 105 annotations.

**Fix found by the tests: `_ts` was never read.** In the first run, `VersioningOfTemplate` failed: the history expiration came back as 1971-01-01. `Entity.LastUpdatedEpochTimestamp` (`_ts`) was get-only (`{ get; }`), so Newtonsoft never set it when deserializing, and it was always `0`. This bug already existed and affected configurations too:

- Every `ConfigurationHistoryEntity.CreationTime` was stored as 1970-01-01.
- The current version's `CreationTime` was 1970-01-01.
- Every history `ExpirationTime` was 1971-01-01.

The property is now `{ get; init; }` (`Backend.CosmosDb/src/Entities/Entity.cs`). Writes are unaffected, because the getter was already serialized and Cosmos DB sets `_ts` itself. History items written before this fix keep their stored 1970 `CreationTime`; only their `ExpirationTime`, which is computed from `_ts` on read, is correct now. `VersioningOfConfiguration` gained assertions on `CreationTime` and `ExpirationTime` to guard against a regression.

### 9. Documentation

- `docs/Platform/Storyteller/templating.md`:
  - Rewritten intro, *Overview*, *Document*, and *Identity* for the project-level identity.
  - New sections: *Earlier storage location*, *Managing templates* (service and HTTP API), and *Versions*.
  - *Cache* updated with the template invalidation table, batching and failure behaviour, and a note that templates written directly into Cosmos are not invalidated.
  - The worked example now refers to `gen.exe` of the project.
- `docs/Platform/Storyteller/inheritance.md`: step 5 of the calculation now points at the project template.

### Build

- `Backend.CosmosDb` and `Backend.CosmosDb.UnitTests` build with no new warnings. Remaining warnings in `CosmosConfigurationService.cs` (CS8625, SA1515, CA2208, CS8602) were already there.
- `Api.Functions` builds with no new warnings. It was built with `-p:OutDir` to a temporary folder, because the running `Api.Functions` process locks `bin/Debug`.
