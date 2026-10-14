# Phase 1 of Unified author and audit actor

## Overview

Phase 1 of [Unified author and audit actor](../2026-10-14%20Unified%20author%20and%20audit%20actor.md) (Linear 424-30) covers sections 1-5 and 7 of the spec: the model, the services, the Cosmos DB storage with the legacy reads, the API, the index policy, the Storyteller documentation and the tests. Every record that names who changed it now uses `AuditActor` and `AuditStamp`, as the annotations already did, and the string author built by `GetAuthor()` is gone. The clients (OpenAPI document, SDKs, `sform`, admin UI) follow in Phase 2. The index policy deviates from the spec after measuring it (section 5).

`dotnet build 42.mono.slnx` succeeds. `Backend.CosmosDb.UnitTests` 333 pass against the Cosmos emulator (328 before, 5 new), and `Api.Functions.UnitTests` 267 pass. One `Api.Functions` run had a failure in `MachineTokenFallbackTests.UserValidatorRejects_MachineValidatorForTheIssuerAccepts`, which passed alone and in the full rerun; the test does not touch the changed code.

## What Was Done

### 1. Model

- `AuditActor.cs`: the comment states the reference (`Kind` + `Id`) and the name snapshot; factory methods `AuditActor.Account(id, name)`, `AuditActor.Machine(id, name)` and `AuditStamp.Of(by, at)`.
- Field changes exactly as in the spec table: `Configuration`, `ConfigurationTemplate`, `ConfigurationSchema` and `ConfigurationVersion` get `Modified` (the version's `CreationTime` is gone); the three summaries get `Modified?` instead of `Author` and `UpdatedAt`; `View` gets `Created?`; `KeyValue` gets `Created`, `Modified`, `Deleted?`; `KeyValueSummary` and `KeyValueVersion` get `Modified`; `KeyValuePolicy` gets `Modified?`; `ClientEncryptionRecipient` gets `Created`; `KeyValueAuditEvent.Actor` is an `AuditActor`.
- The unstored version 0 of a configuration is `Modified = { calculation time, System }` (`Mapping.ToConfiguration(JObject, annotationKey, calculatedAt)`).

### 2. Callers and services

- `IConfigurationService.CreateOrUpdateConfigurationAsync` and `PatchConfigurationAsync` take `AuditActor actor` in place of `string author` and the optional actor; `IConfigurationTemplateService`, `IConfigurationSchemaService` and `IViewService` take `AuditActor actor`.
- `KeyValueCaller(AuditActor Actor, …)`, `WithSecrets(AuditActor)`, `WithoutSecrets(AuditActor)`, `System()`.
- The configuration writes always write the `ConfigurationChanged` audit entry (D4). `CosmosAnnotationAuditTests.ConfigurationChange_IsInTheHistory` and `CosmosAnnotationLifecycleTests.Create_DeletedKey_StartsANewLifetimeWithoutConfiguration` now expect it for writes that passed no actor before.
- `CosmosConfigurationService`, `CosmosConfigurationTemplateService` and `CosmosConfigurationSchemaService` take an optional `TimeProvider` (registered by `AddCosmosDbAnnotations`), so the stamps follow the same clock as the annotations and key/value pairs.
- `CosmosKeyValueAuditLog.BindingReadKey` keys the aggregated binding reads on the `AuditActor`.

### 3. Storage

- Entities store the stamps of their model. `ConfigurationEntity`, `ConfigurationHistoryEntity`, `ConfigurationSchemaEntity`, `ConfigurationSchemaHistoryEntity`, `GenerateTemplateEntity`, `GenerateTemplateHistoryEntity` and `ViewEntity` declare the stamp nullable and keep the replaced fields as nullable legacy properties.
- `LegacyAuthors` (new, internal) parses the old strings; `ConfigurationStampExtensions.GetModified()` (new) and `ViewEntity.GetCreated()` return the stored stamp or the legacy one. The legacy time is the history's `CreationTime`, the view's `CreatedAt`, or the item's `_ts`.
- A write that produces a version sets `Modified` and `Author = null`; the Cosmos client ignores nulls, so the legacy field is dropped. `UpdateViewAsync` converts a legacy view the same way. A history item copies `GetModified()` of the item it archives, so the version keeps the time of its write and no longer takes the `_ts` that the calculation cache moves.
- Listings project `c.Modified` and, for the fallback, `c.Author` and `c._ts` (`ListingRows.cs`, `ListingStamps.ToModified`). `ListingTimestamps` is gone.
- Key/value queries project `c.Modified` and `c.Deleted` and filter on `IS_DEFINED(c.Deleted.At)` (also the usage report). The version lifetime is computed from `Modified.At`.
- System writes use `AuditActor.System`: the calculation cache item, the key/value maintenance and its audit events, and `Create.Configuration` (now `AuditActor? author = null`).

### 4. API

- `GetAuthor()` is removed; `GetSecretRightsAsync`, `ConfigurationHttp`, `ConfigurationSchemaHttp`, `TemplateHttp` and `ViewsHttp` use `GetAuditActor()`.
- `GetAuditActor()` returns the machine first, then, under `DEV_AUTH`, `System` only for a request without a `sub`; otherwise the account with its name, in every build.
- `Api.Web` uses `AuditActor.Account(id, name)`.

### 5. Indexing (deviation)

The spec excluded only the names (`/…/By/Name/?`). Measured on the emulator as the average of 20 creates, that saved little, while the ids and times of the stamps cost most:

| Item | Today's policy | Names excluded | Name and kind excluded | `By` excluded, `At` indexed | Stamps excluded |
| --- | --- | --- | --- | --- | --- |
| Annotation with `Created` and `Modified` | 14.86 | 14.48 | 14.10 | 11.81 | 10.29 |
| Configuration with `Modified` | 11.62 | 11.43 | 11.24 | 10.10 | 9.33 |
| Configuration with the old `Author` string | 9.71 | | | | |

Nothing filters or sorts on the stamps, so `ContainerFactory.AuditStampPaths` excludes them whole: `/Created/*`, `/Modified/*`, `/Deleted/By/*`, `/Deletion/DeletedBy/*`, `/Actor/*`. `/Deleted/At/?` stays indexed for the key/value deleted filter, which therefore tests `c.Deleted.At`, and `/Deletion/DeletedAt/?` for the deleted annotations listing. An annotation write costs about 4.5 RU less than before, and a configuration write with its stamp costs less than one with the old string. A filter on the actor would need the index back, which is an online change. `ApplySettings` adds every missing exclusion, and `EnsureContainerSettingsAsync` applies them to existing containers.

### 7. Documentation

- New `docs/Platform/Storyteller/audit-stamps.md`: the actor, the stamp names and where each record uses them, the storage with the measured charges, and the legacy reads. The spec had planned a section in `audit-operations.md`; a page of its own fits better, because the stamps concern every area and not only the annotation operations. `audit-operations.md` notes the lower write cost.
- `access.md` (the view's `Created`), `annotation-lifecycle.md`, `definitions.md`, `templating.md` (also the stored template example), `inheritance.md`, `key-values.md` and `authentication.md` describe the stamps.

### Tests

- New `Backend.CosmosDb/test/AuditStampTests.cs`: a legacy configuration and history item are read with their parsed actors and times and converted by the next write (the archived version keeps the converted stamp); the version time does not move when the calculation cache rewrites the item; an unwritten configuration is stamped by `System`; a legacy view is read and converted on update; the organization container excludes `AuditStampPaths`.
- The existing tests pass `AuditActor`s and assert the stamps; `SecretRightsTests` asserts the account and the machine actor of the caller.
