# Phase 1 of Soft-delete and Audit of Annotations

## Overview

Phase 1 covers the backend and the data model (sections 1.1-1.12) of [Soft-delete and audit of annotations](../2026-10-11%20Soft-delete%20and%20audit%20of%20annotations.md), Linear 424-6.

- **Lifecycle.** Deleting an annotation is now a soft delete of its whole tree. A deleted annotation can be restored (many keys at once), re-created by a write, deleted permanently, or it expires after 90 days through Cosmos TTL.
- **Audit.** Every create, re-create, update, delete and restore writes an audit entry in the same transactional batch as the annotation. Updates record previous and new values.
- **Validation.** `Title`, `Description` and `DocumentationLink` are validated on the server.
- **Containers.** Organization containers get TTL and keep the audit entries out of the index.

`CosmosAnnotationService` was rewritten around one generic model of the inheritance graph instead of the per-type switch. That fixed the cascade gaps and the responsibility delete that crossed views.

The HTTP endpoints, the SDKs, the CLI and the admin UI are Phases 2-4. Only what was needed to keep the API compiling was touched in `Api.Functions` and `Api.Web` (section 1.5).

## What Was Done

### 1.1 Public model (`Abstractions.Annotations`)

- New `AnnotationLimits` (`Title` 200, `Description` 10,000, `DocumentationLink` 2048), used by `[MaxLength]` on `Annotation`.
- `Model/AuditActor.cs`: `AuditActorKind`, `AuditActor` (with `AuditActor.System`) and `AuditStamp`.
- `Model/AnnotationDeletion.cs`: `DeletionId`, `RootKey`, `DeletedAt`, `DeletedBy`, `IsRoot`, `DescendantCount` and the computed `PurgeAt`.
- `Model/AnnotationAudit.cs`: the audit enums (`AnnotationAuditAction`, `AnnotationAuditCauseKind`, `AnnotationValueChangeKind`, `AnnotationValueKind`, `AnnotationState`) and records (`AnnotationAuditCause`, `AnnotationFieldChange`, `AnnotationLabelChanges`, `AnnotationValueChange`, `AnnotationAuditEntry`, `AnnotationHistory`).
  - **Deviation:** `AnnotationState` and `AnnotationHistory` live in this file, not in a separate `AnnotationHistory.cs`.
  - The truncation flags are omitted from the stored JSON when `false`.
- `Model/AnnotationOperationResults.cs`: `AnnotationKeysRequest`, `AnnotationRestoreRequest`, `AnnotationKeyFailure`, `AnnotationDeletionResult`, `AnnotationRestoreResult` and `AnnotationPurgeResult`.
- `Annotation` and `IAnnotation` have `Created`, `Modified` and `Deletion`, plus `[MaxLength]` on the three text fields.
- `AnnotationKeyExtensions` gained:
  - `GetDirectAncestorKeys()`, the parents in merge order;
  - `GetAllAncestorKeys()`, roots first and de-duplicated;
  - `GetDepth()`, 0 to 3;
  - `IsDescendantOf(ancestor)`.

### 1.2 Entities (`Backend.CosmosDb`)

- `AnnotationEntity` has `Created`, `Modified`, `Deletion` and a nullable `ttl`.
- `ConfigurationEntity` has `AnnotationDeletionId` and a nullable `ttl`.
- New `AnnotationAuditEntity` (`{view}.adt.{key}`): `AuditedType`, `Entries`, `IsTrimmed`, `SizeInBytes` and a nullable `ttl`. `EntityIdPrefixTypes.AnnotationAudit = "adt"`.
- `Created` and `Modified` stay nullable on the model, because `Annotation` is also the body of every write; every stored annotation has them. Follow-up of 2026-10-12: DbCreator's seed (`Backend.CosmosDb/src/Create.cs`) stamps its annotations with `AuditActor.System`, and its unit-of-execution helper, unused so far, now writes the type `UnitOfExecution` instead of `Execution`.
- `AnnotationMappings.ToModel` copies the stamps and the deletion, and computes `PurgeAt` as `_ts + ttl` with the new `DeletionRetention.GetPurgeAt`. `ToEntity` never copies them: the service sets them.

### 1.3 Container settings

- `ContainerFactory.CreateContainerIfNotExistsAsync` creates `org.*` containers with `DefaultTimeToLive = -1`, the default included path and `/Entries/*` excluded. The `core` container is created as before.
- New `IContainerFactory.EnsureContainerSettingsAsync(Container)` (read, apply, replace when changed) and `EnsureOrganizationContainersAsync()`. DbCreator's `CoreDbStructureBuilder` calls the latter on every start.
- Organization creation (`CosmosAccessService`) gets the settings through `CreateContainerIfNotExistsAsync`.
- The test `OrganizationContainer_HasTtlAndKeepsAuditEntriesOutOfTheIndex` confirms that the emulator accepts the replace.

### 1.4 Validation

- `Backend.Core/src/Annotating/AnnotationRules.cs`:
  - `Validate` and `ValidateAll` (one exception that lists every invalid key);
  - `GetProblems`.
- `DocumentationLink` must be an absolute `http`/`https` URL **without whitespace**, the same as the UI's check. Violations throw `InvalidInputException` with `ErrorCodes.InvalidAnnotation`.
- New error codes: `InvalidAnnotation`, `InvalidKey` (for Phase 2), `AnnotationDeleted` and `AnnotationNotDeleted`.

### 1.5 Service contract and the write path

- `IAnnotationService` is the contract of the spec, with an `AuditActor` on every write.
  - New: `GetDeletedAnnotationAsync`, `FindDeletedAnnotationsAsync` (one `ReadManyItemsStreamAsync`), `SetAnnotationAsync`, `RestoreAnnotationsAsync`, `PurgeAnnotationAsync` and `GetAnnotationHistoryAsync`.
  - `AnnotationsRequest` gained `Deleted` (`AnnotationDeletedFilter`: `Exclude`, the default, `Only` or `Include`), `DeletionRootsOnly` and `DeletionId`. `GetAnnotationsAsync` applies them with `IS_DEFINED(c.Deletion)`.
- **New building blocks** in `Backend.CosmosDb/src/Annotating/`:

  | Type | Role |
  | --- | --- |
  | `AnnotationItemSerializer` | System.Text.Json for annotations and audits, Newtonsoft for configurations |
  | `AnnotationItemStore` | Point reads with ETags; the descendant query; `ReadManyAsync`; `ReadStateAsync`, used by the configuration service |
  | `AnnotationNode` | The annotation, configuration and audit of one key |
  | `DescendantScope` | The partitions and key prefixes of a subtree |
  | `AnnotationRelations` | The parent arrays as data |
  | `BatchPlan` | Transactional batches in waves, at most 100 operations, failures checked |
  | `AnnotationConcurrencyException` | Retried up to 3 times, then `ConflictException` |
- `Backend.Core/src/Annotating/AnnotationFactory.cs` builds models whose key-derived members follow the key:
  - `CreateDefault`, with the automatic-ancestor copy of `ValidFrom`, `ExpiresAt`, `TimeZone`, `IsDisabled` and `Labels`;
  - `Normalize`.

  A body's `Name`, type and ancestor keys are now taken from the key, and the relation arrays from the store.
- **Create and re-create** (`CreateCoreAsync`): loads the key and all its ancestors, then:
  - creates the missing ones and re-creates the deleted ones;
  - for a re-created annotation, upserts its configuration content as the version snapshot, deletes the configuration, and appends `Recreated` to the existing audit;
  - adds names to the parent arrays;
  - marks the children deleted with a re-created annotation as roots.

  A create of an active key throws `ConflictException`.

  **Follow-up of 2026-10-12:** the create reads only the annotation item of each key first. The configuration and the audit are read only for keys that are not active: the audit for a missing key (an earlier one may remain), both for a deleted key. An existing ancestor now costs one point read instead of three. `SetAnnotationAsync` updates an active key and creates otherwise.
- **Update** (`UpdateCoreAsync`): `AnnotationAuditDiff` against the stored entity. No change writes nothing. Otherwise the entity is replaced with `IfMatchEtag`, together with the audit. The stored relation arrays and `Created` are kept.
- **Callers:**
  - `AnnotationsHttp` passes `request.GetAuditActor()` to every write. `HttpRequestDataExtensions.GetAuditActor()` (spec 2.2) was added in this phase so the API compiles: machine, or account with the `name` claim (falling back to the unique name), or `System` under `DEV_AUTH`. `SetAnnotation` still calls `ExistAnnotationAsync` and then create or update, and returns the request body; Phase 2 switches it to `SetAnnotationAsync`.
  - `Api.Web` builds the actor from its `ClaimsPrincipal`.
  - The test doubles in `Api.Functions/test` and `Backend.CosmosDb/test` implement the new contract.
  - The existing Cosmos tests call the old signatures through `AnnotationServiceTestExtensions`, which supplies a test actor.

### 1.6 Soft delete and 1.6a automatic purge

- `DeleteCoreAsync` follows the spec:
  1. Loads the root, then the subtree, in one query per scope.
  2. Reuses the `DeletionId` (and `DeletedAt`) of an unfinished delete of the same root.
  3. Writes per annotation: entity with `Deletion` and `ttl`; configuration patch (`AnnotationDeletionId`, `ttl`) plus the version snapshot when it has content; `Deleted` audit entry with `ttl`. The waves run deepest first and the root last.
  4. Removes the names from active parents outside the tree.
- The new cascade covers the whole tree for every type. Units of execution are now deleted with an execution or a context, and a deleted unit leaves `Execution.UnitNames`. A responsibility delete no longer empties the partition of every view (test `Delete_Responsibility_LeavesTheSameResponsibilityInOtherViews`).
- `DeletionRetention.GetRemainingSeconds(deletedAt, retentionDays, now)` gives the `ttl`. `AnnotationOptions.DeletedRetentionDays` defaults to 90 and is validated between 1 and 365 (`Annotations` section).
- Every later write to a deleted item recomputes the `ttl`: the delete itself, and the `IsRoot` marks of a restore or a re-create.
- **Deviation:** the stored procedures `removeValueFromArray` and `deleteUsageAndExecutions` are no longer used. The arrays are computed in memory and written with ETags. The procedures stay registered, so the spec's change to make `removeValueFromArray` tolerant was not needed.

### 1.7 Restore

- `RestoreAnnotationsAsync` sorts the keys by depth, skips keys already restored in the same request, and reports `NotFound` (and other domain errors) per key in `Failed`.
- `RestoreCoreAsync`:
  - restores deleted ancestors alone and creates missing ones;
  - restores the key, and with descendants every descendant of the same deletion;
  - patches the configurations: removes the stamp and `ttl`, clears `CalculatedContent` and its hash, and deletes the snapshot version in the same batch;
  - prunes the arrays of annotations restored alone and marks their children as roots;
  - adds the restored names to active parents.
- A retry after a partial restore keeps the first attempt's `DeletionId`.
- **Found by a test:** a restored descendant can have an active parent outside the restored tree (the context of a restored usage's execution). Such parents are loaded before the arrays are updated (`Restore_Usage_PutsItsExecutionsBackIntoTheirContexts`).
- **Deviation (I4):** an annotation deleted separately before its ancestor keeps `IsRoot = true` when the ancestor is deleted later. It stays a root of its own deletion, so the deleted list can show it next to its ancestor. Clearing it would need a subtree query on every delete and its counterpart on every restore. A restore of either works as specified.

### 1.8 Purge

- `PurgeCoreAsync` throws `NotFoundException` for a missing key and `ConflictException(AnnotationNotDeleted)` for an active one, and refuses when a descendant was restored concurrently.
- It deletes, deepest first: the configuration (after upserting its content as a version), the annotation (with `IfMatchEtag`) and the audit.
- `DeleteAllItemsByPartitionKeyStreamAsync` is gone.

### 1.9 Audit document

- `Backend.Core/src/Annotating/AnnotationAuditDiff.cs` computes `Fields`, `Labels` and `Values` as decided:
  - previous and new values, with strings cut at 256;
  - `Description` and `UnitDefinition` only flagged;
  - `null` equal to `false` for `IsDisabled`, and empty equal to absent;
  - values compared as JSON (`JsonElement.DeepEquals`), primitives kept, objects and arrays by kind.
- `AnnotationAuditStore`:
  - `NewEntry` (Guid v7 ids);
  - `Prepare`: appends, drops the oldest entries above `Audit:MaxDocumentBytes` (64 KiB, see below) minus a 200-byte allowance, measured on the serialized bytes, and settles `SizeInBytes`;
  - `Write`: create, or replace with `IfMatchEtag`;
  - `AppendAsync`: a standalone append with retries, for configuration events.

  `AnnotationAuditOptions` (`Audit` section) is validated: 1 KiB to 1.5 MiB, value length at least 1.
- Configuration events: `CreateOrUpdateConfigurationAsync`, `PatchConfigurationAsync`, `ClearConfigurationAsync` and `DeleteAsync` take an optional `AuditActor` and append `ConfigurationChanged` or `ConfigurationDeleted`. It is best effort: failures are logged. Phase 2 passes the actor from the endpoints.
- **RU charge**, measured on the emulator with `/Entries/*` excluded, replacing an audit document of typical entries:

  | Size | Entries | Replace | Read |
  | --- | --- | --- | --- |
  | 1 KiB | 2 | 10.6 RU | 1.0 RU |
  | 16 KiB | 35 | 15.1 RU | 1.3 RU |
  | 64 KiB | 142 | 25.7 RU | 2.2 RU |
  | 128 KiB | 285 | 52.1 RU | 4.8 RU |
  | 256 KiB | 571 | 130.9 RU | 10.0 RU |

  An update of an annotation whose audit is full therefore costs about 60 RU more than one with a fresh audit. The emulator only approximates the service's charges.

  **Follow-up of 2026-10-12:** the default was lowered to **64 KiB** (about 140 typical entries). The worst update drops from about 70 RU to about 40 RU.

### 1.10 History read

`GetAnnotationHistoryAsync`:
- returns the entries newest first, with the state (`Active` or `Deleted`) and `IsTrimmed`;
- answers `null` for a missing or purged key;
- needs no derivation, because the values are stored.

### 1.11 Reads and configurations of deleted annotations

- **Annotation reads.** `ExistAnnotationAsync`, `GetAnnotationAsync` and the list endpoints see active annotations only. `@annotation` bindings follow through `GetAnnotationAsync`.
- **Configuration reads.**
  - `GetRawConfigurationAsync` (and the resolved and view-diff reads built on it), `GetConfigurationHierarchyViewAsync` and `HasConfigurationContentAsync` treat a stamped configuration or a deleted annotation as missing.
  - `GetConfigurationVersionsAsync` and `GetConfigurationVersionContentAsync` hide the versions while the annotation is deleted.
  - `ListConfigurationsAsync` filters `NOT IS_DEFINED(c.AnnotationDeletionId)`.
- **Deviation:** the reads return `null` or empty instead of throwing `NotFoundException(AnnotationDeleted)`. The service contract keeps "null = no configuration", and the existing tests rely on it. Phase 2 sets `ErrorCode = AnnotationDeleted` on the `404` by asking `GetDeletedAnnotationAsync` when a read finds nothing.
- **Configuration writes.** `CreateOrUpdate`, `Patch`, `Clear` and `Delete` throw `ConflictException(AnnotationDeleted)`. The annotation is read only when there is no configuration item.
- **Cache invalidation.** `ConfigurationCacheInvalidator` skips stamped configurations, so it never pushes back their expiry.
- **Deviation:** history writes stay `CreateItem`, because `PatchConfigurationAsync` relies on its `409` to detect concurrent writers. The version snapshot is instead deleted inside the restore batch, guarded by the stamp: the snapshot was written in the same batch as the stamp, so it exists exactly when the stamp and content do.
- `IConfigurationService.DeleteWithDescendantsAsync` and its helpers were removed; nothing called them any more.

### 1.12 Tests

| Project | Tests |
| --- | --- |
| **New `Backend.Core/test`** (xUnit, Shouldly; in `42.mono.slnx`) | 34: `AnnotationRulesTests`, `AnnotationAuditDiffTests` and `AnnotationModelTests` (ancestors, `IsDescendantOf`, `Normalize`, `CreateDefault`, retention). |
| **`Backend.CosmosDb/test`** | 26 new, in `CosmosAnnotationLifecycleTests` (19) and `CosmosAnnotationAuditTests` (7). They cover: delete of each shape; idempotent delete; TTL on, then off; restore with and without descendants; restore of a descendant; restore of an expired ancestor; outside parents; failures per key; re-create and its configuration numbering; create below a deleted ancestor; purge; lookup; hidden and read-only configurations; validation; container settings; update values and actors; no-op updates; server-owned arrays; ancestor causes; configuration events; trimming and `SizeInBytes`. |
| **Totals** | `Backend.CosmosDb` 257 (231 before), `Backend.Core` 34, `Api.Functions` 146. All pass. |

### Documentation

- New [annotation-lifecycle.md](../../annotation-lifecycle.md): states, items, delete, restore, re-create, purge and expiry, the audit and its size query, containers, validation and error codes.
- [inheritance.md](../../inheritance.md): configuration reads of deleted annotations, and cache handling on restore.

### Notes for Phase 2

- `AnnotationsHttp.GetDescendants` builds `AnnotationsRequest.Condition<T>` without `Against`, so the conditions never apply. The endpoint returns everything of the requested types in the partition, or in the project for subjects and contexts. Phase 2 should base it on `IsDescendantOf` / `DescendantScope`.
- The bulk `SetAnnotations` binds `List<Annotation>` to the base type. `AnnotationFactory.Normalize` now turns a base `Annotation` into the key's runtime type, so the bulk create no longer depends on the body's type.
