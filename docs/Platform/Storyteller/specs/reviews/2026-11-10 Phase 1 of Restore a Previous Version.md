# Phase 1 of Restore a Previous Version

## Overview

Phase 1 of [Restore a Previous Version of a Configuration, Template or Schema](../2026-11-10%20Restore%20a%20Previous%20Version%20of%20a%20Configuration,%20Template%20or%20Schema.md) (Linear 424-221): the models, the service members and their Cosmos DB implementations, with their tests. A restore stores the content of version N, whole and as stored, as a new version marked `RestoredFrom = N`; the current version, or one whose content the current version has, changes nothing. The spec's sections 1 to 4 and the backend part of section 11.

## What Was Done

### 1. The restore rules

Implemented as the spec states them, in this order:

- **Configurations:** the annotation state first (`Deleted` → `ConflictException(AnnotationDeleted)`, `Missing` → `NotFoundException(AnnotationNotFound)`), then per attempt the row, the no-op of the current version, `VersionNotFound` for a version above the current one (the leftover rule of the version list), the archive N (read once, a written version by `IsWrittenVersion`), the no-op of equal content, the schema check unless forced (once), and the write.
- **Templates:** per attempt the template and its state; the no-ops finish a pending cache invalidation, as an equal write does.
- **Schemas:** for the two keyed kinds the pair rule first (descendant types, `InvalidKey` whatever `force` is), then the annotation state, then per attempt the no-ops, `EnsureValidSchemaAsync` (once, whatever `force` is), the compliance check unless forced, and the write.
- A version is never re-read per attempt: archives and history items are written once. The current document is read again by every attempt, so a concurrent write is retried as the writes retry it, and four lost attempts end as they do (`ConflictException(Conflict)`, `TemplateConcurrencyException`, `SchemaConcurrencyException`).

### 2. Models

- `VersionRestoreResult` (`Abstractions.Annotations/src/Model/VersionRestoreResult.cs`) with the factories `Restored` and `Unchanged`, and `VersionRestoreOutcome` in its own file (`VersionRestoreOutcome.cs`), as StyleCop wants one type per file whose name matches.
- `ConfigurationVersion.RestoredFrom` (`uint?`, as `Version`).
- `AnnotationAuditAction.ConfigurationRestored`, appended, and `AnnotationAuditEntry.FromVersion` (`ulong?`).

### 3. Services

- `ErrorCodes.VersionNotFound`.
- `IConfigurationService.RestoreConfigurationVersionAsync`, `IConfigurationTemplateService.RestoreTemplateVersionAsync`, and `IConfigurationSchemaService.RestoreSchemaVersionAsync`, `RestoreAnnotationSchemaVersionAsync`, `RestoreDescendantTypeSchemaVersionAsync`, with comments on their exceptions. The annotation key is the fourth argument of the keyed schema members.
- `NotSupportedException` stubs in `FakeConfigurationService` (`TransferFakes.cs`), `FakeTemplateService` and `FakeSchemaService`.

### 4. Cosmos DB

- **Entities:** `RestoredFrom` (`ulong?`) on `ConfigurationEntity`, `ConfigurationHistoryEntity`, `GenerateTemplateEntity`, `GenerateTemplateHistoryEntity`, `ConfigurationSchemaEntity` and `ConfigurationSchemaHistoryEntity`. `ToHistory` of each document, `CosmosConfigurationService.ToHistory`, `CosmosAnnotationService.ToHistorySnapshot` (the soft delete, the re-create and the purge) and every `ToConfigurationVersion` carry it.
- **Configurations:** `CosmosConfigurationService.Restore.cs`. The create of a row moved into `TryCreateVersionAsync`, which `WriteConfigurationAsync` uses too. `TryWriteNextVersionAsync` takes `restoredFrom` and sets it on the copied row, so an ordinary write after a restore does not inherit the mark (the row is copied with `with`). `AuditConfigurationAsync` takes `fromVersion`. After the commit: `ConfigurationRestored` (best effort), then `CompleteInvalidationAsync`.
- **Templates:** `RestoreTemplateVersionAsync` in `CosmosConfigurationTemplateService`. The create batch moved into `TryCreateVersionAsync`, used by `WriteTemplateAsync` too; `TryWriteNextVersionAsync` takes `restoredFrom` and resets it otherwise.
- **Schemas:** `CosmosConfigurationSchemaService.Restore.cs`. `SaveAsync` now uses two helpers the restore shares: `CheckComplianceAsync` (the compliance check with its force rules and logs) and `TryWriteVersionAsync` (the batch of the history, the new current item and the state). `EnsureAnnotationRestorableAsync` reads the annotation state with `AnnotationItemStore.ReadStateAsync`; the schema writes still do not check it.
- Not in the spec: the configuration restore validates the schema once, not on every attempt, as its content does not change between attempts.

### 11. Tests

All with `STORYTELLER_TESTS_USE_CONTAINER=true`:

- `CosmosConfigurationRestoreTests` (12): an old version stored whole (no merge) as a new version with `RestoredFrom`, and an ordinary write after it without; the audit entry (`ConfigurationRestored`, `ConfigurationVersion`, `FromVersion`, the actor); the current version and an equal one change nothing and audit nothing; the restore after a clear brings the configuration back, continues the numbering and clears the descendant's cached calculation; the version that `DeleteAsync` archived is written again; unknown, expired (the archive deleted) and hidden (an archive above the current version) versions are `VersionNotFound`; a soft-deleted annotation is `AnnotationDeleted`; a purged and an unknown one `AnnotationNotFound`, while the purge keeps the versions with their mark; the schema check and `force`; a stored literal `$remove` copied as it is; a concurrent change retried once, and four lost attempts `Conflict`.
- `CosmosTemplateRestoreTests` (6): an old version stored whole, `RestoredFrom` and its reset; the no-ops; a deleted template created again after the last version; unknown, never written and expired versions; the cleared calculations; a stored literal `$patch` copied as it is.
- `CosmosSchemaRestoreTests` (7): the type schema restore, its marks and the no-ops; a deleted schema created again and an unknown version; the compliance check and `force`; a version that is no usable schema (an external `$ref` planted in its history item) refused with `ExternalSchemaReference` even when forced; the annotation schema with an active, a soft-deleted, a purged and an unknown annotation; the descendant-type schema with the pair rule and a missing annotation.
- The suites around them pass unchanged: `CosmosConfigurationServiceTests`, `CosmosConfigurationRecoveryTests`, `CosmosConfigurationInvalidationOrderTests`, `CosmosConfigurationTemplateServiceTests`, `CosmosConfigurationSchemaServiceTests`, `CosmosListingTests`, `CosmosAnnotationLifecycleTests`, `CosmosAnnotationAuditTests`, `CosmosViewImportTests`, `CosmosViewTransferTests`, `CosmosConfigurationCompositionTests` (227 tests), and `Backend.Core` (1362).
